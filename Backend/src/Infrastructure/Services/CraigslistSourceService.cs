using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Application.Domain.Entities;
using Application.Domain.Enums;
using Application.Models.Craigslist;
using Application.Ports;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Infrastructure.Services;

/// <summary>
/// <see cref="IListingSource"/> implementation backed by Craigslist's public per-search RSS
/// feeds (e.g. https://newyork.craigslist.org/search/ela?format=rss&amp;query=iphone). This
/// deliberately avoids scraping Craigslist's HTML/ToS risk - the RSS feed is a publicly
/// documented, unauthenticated endpoint, so this service is much simpler than
/// <see cref="EbaySourceService"/> (no OAuth, no options credentials).
///
/// ASSUMPTION (v0, documented): Craigslist is organized by city subdomain (e.g.
/// "newyork", "losangeles"), but <see cref="Watch.Location"/> is a free-text string in this
/// domain model. For v0 we assume <see cref="Watch.Location"/> is ALREADY a Craigslist city
/// subdomain slug (lowercase, no spaces - e.g. "newyork", not "New York, NY"). There is no
/// full city-name-to-subdomain mapping table here; that's future work if/when Location needs
/// to support arbitrary free-text city names. We do a light normalization (lowercase, strip
/// whitespace) but nothing beyond that.
/// </summary>
public class CraigslistSourceService : IListingSource
{
    private readonly HttpClient _httpClient;
    private readonly CraigslistOptions _options;
    private readonly ILogger<CraigslistSourceService> _logger;

    // Small hardcoded Category -> Craigslist "for sale" category code mapping, scoped to
    // categories relevant to the phone/electronics reseller vertical this product targets.
    // Craigslist has dozens of category codes; this is NOT a complete mapping and is a
    // documented v0 limitation. Unmapped categories fall back to "sss" (all "for sale").
    private static readonly Dictionary<string, string> CategoryCodeMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["electronics"] = "ela",
        ["cell phones"] = "moa",
        ["cell-phones"] = "moa",
        ["phones"] = "moa",
        ["computers"] = "sya",
        ["video games"] = "vga",
        ["video-games"] = "vga",
    };

    private const string DefaultCategoryCode = "sss"; // "for sale - all"

    public CraigslistSourceService(HttpClient httpClient, IOptions<CraigslistOptions> options, ILogger<CraigslistSourceService> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<IReadOnlyList<Listing>> SearchAsync(Watch watch, CancellationToken cancellationToken = default)
    {
        try
        {
            var citySlug = NormalizeLocationToCitySlug(watch.Location);
            if (string.IsNullOrEmpty(citySlug))
            {
                _logger.LogWarning("Craigslist search skipped for watch {WatchId}: no usable Location", watch.Id);
                return Array.Empty<Listing>();
            }

            var categoryCode = MapCategoryToCode(watch.Category);
            var requestUrl = string.Format(
                CultureInfo.InvariantCulture,
                _options.SearchUrlTemplate,
                citySlug,
                categoryCode,
                Uri.EscapeDataString(watch.Keywords));

            using var response = await _httpClient.GetAsync(requestUrl, cancellationToken).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                // Deliberate resilience choice, mirroring EbaySourceService: a transient
                // Craigslist failure for one watch should not block a poller from processing
                // the remaining watches.
                _logger.LogWarning(
                    "Craigslist search request failed with status {StatusCode} for watch {WatchId}",
                    response.StatusCode,
                    watch.Id);
                return Array.Empty<Listing>();
            }

            var xml = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            return MapFeedToListings(xml, watch.Id);
        }
        catch (Exception ex)
        {
            // Deliberate resilience choice, mirroring EbaySourceService: don't let one watch's
            // transient failure (network error, malformed XML, etc.) block a poller that is
            // processing other watches.
            _logger.LogWarning(ex, "Craigslist search failed unexpectedly for watch {WatchId}", watch.Id);
            return Array.Empty<Listing>();
        }
    }

    /// <summary>
    /// v0 Location normalization: lowercase + strip whitespace. Assumes Location is already
    /// (or trivially reducible to) a Craigslist city subdomain slug - see class remarks.
    /// </summary>
    public static string NormalizeLocationToCitySlug(string? location)
    {
        if (string.IsNullOrWhiteSpace(location))
        {
            return string.Empty;
        }

        return location.Trim().ToLowerInvariant().Replace(" ", string.Empty);
    }

    public static string MapCategoryToCode(string? category)
    {
        if (string.IsNullOrWhiteSpace(category))
        {
            return DefaultCategoryCode;
        }

        return CategoryCodeMap.TryGetValue(category.Trim(), out var code) ? code : DefaultCategoryCode;
    }

    /// <summary>
    /// Pure mapping logic from a Craigslist RSS feed XML payload to domain <see cref="Listing"/>
    /// entities. Uses <see cref="System.Xml.Linq"/> rather than
    /// <c>System.ServiceModel.Syndication.SyndicationFeed</c>: Craigslist's RSS feed titles
    /// embed the price/location in ways that are easiest to pull out with direct element
    /// access, and this avoids an extra NuGet dependency for a feed format that is simple
    /// standard RSS 2.0. Kept as a public static method (no HTTP required) so it can be unit
    /// tested directly with a hand-built RSS string, mirroring
    /// <see cref="EbaySourceService.MapItemsToListings"/>.
    /// </summary>
    public static List<Listing> MapFeedToListings(string rssXml, Guid watchId)
    {
        var listings = new List<Listing>();

        if (string.IsNullOrWhiteSpace(rssXml))
        {
            return listings;
        }

        XDocument doc;
        try
        {
            doc = XDocument.Parse(rssXml);
        }
        catch (System.Xml.XmlException)
        {
            return listings;
        }

        var items = doc.Descendants("item");

        foreach (var item in items)
        {
            var title = item.Element("title")?.Value ?? string.Empty;
            var link = item.Element("link")?.Value ?? string.Empty;
            var pubDateRaw = item.Element("pubDate")?.Value;

            DateTimeOffset? postedAt = null;
            if (!string.IsNullOrEmpty(pubDateRaw) &&
                DateTimeOffset.TryParse(pubDateRaw, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsedDate))
            {
                postedAt = parsedDate;
            }

            listings.Add(new Listing
            {
                Title = title,
                Price = ParseLeadingPrice(title),
                Url = link,
                Source = ListingSource.Craigslist,
                PostedAt = postedAt,
                MatchedWatchId = watchId,
                CreatedAt = DateTime.UtcNow
            });
        }

        return listings;
    }

    // Craigslist RSS titles commonly look like "$50 - iPhone 12 64GB" or just "iPhone 12
    // 64GB" when no price was set. Parse a leading "$<number>" if present, else null.
    private static readonly Regex LeadingPriceRegex = new(@"^\s*\$\s*([0-9][0-9,]*(?:\.[0-9]+)?)", RegexOptions.Compiled);

    public static decimal? ParseLeadingPrice(string title)
    {
        if (string.IsNullOrEmpty(title))
        {
            return null;
        }

        var match = LeadingPriceRegex.Match(title);
        if (!match.Success)
        {
            return null;
        }

        var numeric = match.Groups[1].Value.Replace(",", string.Empty);
        return decimal.TryParse(numeric, NumberStyles.Number, CultureInfo.InvariantCulture, out var price)
            ? price
            : null;
    }
}
