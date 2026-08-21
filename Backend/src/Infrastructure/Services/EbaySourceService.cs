using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Application.Domain.Entities;
using Application.Domain.Enums;
using Application.Models.Ebay;
using Application.Ports;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Infrastructure.Services;

/// <summary>
/// <see cref="IListingSource"/> implementation backed by eBay's Buy Browse API,
/// using the OAuth2 client-credentials flow.
/// </summary>
public class EbaySourceService : IListingSource
{
    private readonly HttpClient _httpClient;
    private readonly EbayOptions _options;
    private readonly ILogger<EbaySourceService> _logger;

    private readonly SemaphoreSlim _tokenSemaphore = new(1, 1);
    private string? _accessToken;
    private DateTimeOffset _tokenExpiresAt;

    public EbaySourceService(HttpClient httpClient, IOptions<EbayOptions> options, ILogger<EbaySourceService> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<IReadOnlyList<Listing>> SearchAsync(Watch watch, CancellationToken cancellationToken = default)
    {
        // Token acquisition failures (e.g. misconfigured credentials) are intentionally
        // NOT caught here - they must surface loudly via InvalidOperationException rather
        // than being swallowed into a silent "zero listings found" result, which would
        // hide a configuration problem behind what looks like a normal empty search.
        var accessToken = await EnsureAccessTokenAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            var queryBuilder = new StringBuilder();
            queryBuilder.Append("q=").Append(Uri.EscapeDataString(watch.Keywords));

            if (!string.IsNullOrEmpty(watch.Category))
            {
                queryBuilder.Append("&category_ids=").Append(Uri.EscapeDataString(watch.Category));
            }

            var filter = BuildPriceFilter(watch.MinPrice, watch.MaxPrice);
            if (!string.IsNullOrEmpty(filter))
            {
                queryBuilder.Append("&filter=").Append(Uri.EscapeDataString(filter));
            }

            // TODO: Watch.Location isn't currently mapped to an eBay Browse API filter -
            // there's no simple free-text location param that fits its shape.
            var requestUrl = $"{_options.BrowseApiBaseUrl}/item_summary/search?{queryBuilder}";

            using var request = new HttpRequestMessage(HttpMethod.Get, requestUrl);
            request.Headers.Add("X-EBAY-C-MARKETPLACE-ID", _options.MarketplaceId);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

            using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                // Deliberate resilience choice: a transient eBay API failure for one watch
                // should not block a poller from processing the remaining watches.
                _logger.LogWarning(
                    "eBay search request failed with status {StatusCode} for watch {WatchId}",
                    response.StatusCode,
                    watch.Id);
                return Array.Empty<Listing>();
            }

            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            var searchResponse = JsonSerializer.Deserialize<EbaySearchResponse>(body);

            return searchResponse is null
                ? Array.Empty<Listing>()
                : MapItemsToListings(searchResponse, watch.Id);
        }
        catch (Exception ex)
        {
            // Deliberate resilience choice: don't let one watch's transient eBay API
            // failure (network error, deserialization issue, etc.) block a poller
            // that is processing other watches. Note this catch is scoped to the search
            // request/parsing only - token acquisition failures are handled above and
            // are allowed to propagate.
            _logger.LogWarning(ex, "eBay search failed unexpectedly for watch {WatchId}", watch.Id);
            return Array.Empty<Listing>();
        }
    }

    private static string? BuildPriceFilter(decimal? minPrice, decimal? maxPrice)
    {
        if (minPrice.HasValue && maxPrice.HasValue)
        {
            return $"price:[{minPrice.Value.ToString(CultureInfo.InvariantCulture)}..{maxPrice.Value.ToString(CultureInfo.InvariantCulture)}],priceCurrency:USD";
        }

        if (minPrice.HasValue)
        {
            return $"price:[{minPrice.Value.ToString(CultureInfo.InvariantCulture)}..],priceCurrency:USD";
        }

        if (maxPrice.HasValue)
        {
            return $"price:[..{maxPrice.Value.ToString(CultureInfo.InvariantCulture)}],priceCurrency:USD";
        }

        return null;
    }

    private async Task<string> EnsureAccessTokenAsync(CancellationToken cancellationToken)
    {
        if (_accessToken is not null && DateTimeOffset.UtcNow < _tokenExpiresAt)
        {
            return _accessToken;
        }

        await _tokenSemaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_accessToken is not null && DateTimeOffset.UtcNow < _tokenExpiresAt)
            {
                return _accessToken;
            }

            var credentials = Convert.ToBase64String(
                Encoding.UTF8.GetBytes($"{_options.ClientId}:{_options.ClientSecret}"));

            using var request = new HttpRequestMessage(HttpMethod.Post, _options.OAuthTokenUrl);
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", credentials);
            request.Content = new StringContent(
                "grant_type=client_credentials&scope=https://api.ebay.com/oauth/api_scope",
                Encoding.UTF8,
                "application/x-www-form-urlencoded");

            using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError(
                    "Failed to acquire eBay access token. Status: {StatusCode}, Reason: {ReasonPhrase}",
                    response.StatusCode,
                    response.ReasonPhrase);
                throw new InvalidOperationException(
                    $"Failed to acquire eBay access token: {(int)response.StatusCode} {response.ReasonPhrase}");
            }

            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            var tokenResponse = JsonSerializer.Deserialize<EbayTokenResponse>(body);

            if (tokenResponse?.AccessToken is null)
            {
                _logger.LogError("Failed to acquire eBay access token: response did not contain an access token");
                throw new InvalidOperationException("Failed to acquire eBay access token: no access_token in response");
            }

            _accessToken = tokenResponse.AccessToken;
            _tokenExpiresAt = DateTimeOffset.UtcNow.AddSeconds(tokenResponse.ExpiresIn - 60);

            return _accessToken;
        }
        finally
        {
            _tokenSemaphore.Release();
        }
    }

    /// <summary>
    /// Pure mapping logic from an eBay Browse API search response to domain <see cref="Listing"/>
    /// entities. Kept as a public static method (no HTTP/mocking required) so it can be
    /// unit tested directly with a hand-built <see cref="EbaySearchResponse"/>.
    /// </summary>
    public static List<Listing> MapItemsToListings(EbaySearchResponse response, Guid watchId)
    {
        var listings = new List<Listing>();

        if (response.ItemSummaries is null)
        {
            return listings;
        }

        foreach (var item in response.ItemSummaries)
        {
            listings.Add(new Listing
            {
                Title = item.Title ?? string.Empty,
                Price = decimal.TryParse(item.Price?.Value, NumberStyles.Number, CultureInfo.InvariantCulture, out var price)
                    ? price
                    : null,
                Url = item.ItemWebUrl ?? string.Empty,
                Source = ListingSource.eBay,
                PostedAt = null, // Browse API item_summary/search does not reliably return a
                                  // per-item creation/posted timestamp; always leave null in
                                  // this implementation (documented limitation, not a bug).
                MatchedWatchId = watchId,
                CreatedAt = DateTime.UtcNow
            });
        }

        return listings;
    }

    public class EbayTokenResponse
    {
        [JsonPropertyName("access_token")]
        public string? AccessToken { get; set; }

        [JsonPropertyName("expires_in")]
        public int ExpiresIn { get; set; }

        [JsonPropertyName("token_type")]
        public string? TokenType { get; set; }
    }

    public class EbaySearchResponse
    {
        [JsonPropertyName("itemSummaries")]
        public List<EbayItemSummary>? ItemSummaries { get; set; }

        [JsonPropertyName("total")]
        public int Total { get; set; }
    }

    public class EbayItemSummary
    {
        [JsonPropertyName("itemId")]
        public string? ItemId { get; set; }

        [JsonPropertyName("title")]
        public string? Title { get; set; }

        [JsonPropertyName("price")]
        public EbayPrice? Price { get; set; }

        [JsonPropertyName("itemWebUrl")]
        public string? ItemWebUrl { get; set; }

        [JsonPropertyName("itemCreationDate")]
        public DateTimeOffset? ItemCreationDate { get; set; }
    }

    public class EbayPrice
    {
        [JsonPropertyName("value")]
        public string? Value { get; set; }

        [JsonPropertyName("currency")]
        public string? Currency { get; set; }
    }
}
