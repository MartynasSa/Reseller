using System.Globalization;
using System.Text;
using Application.Domain.Entities;
using Application.Domain.Enums;
using Application.Models.Telegram;
using Application.Ports;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Infrastructure.Services;

/// <summary>
/// <see cref="INotificationSender"/> implementation backed by the Telegram Bot API's
/// <c>sendMessage</c> method - a single unauthenticated-transport HTTP POST (the bot token is
/// embedded in the URL path) to <c>{ApiBaseUrl}{BotToken}/sendMessage</c> with <c>chat_id</c>
/// and <c>text</c> form fields. No OAuth flow, so this is much simpler than
/// <see cref="EbaySourceService"/> and closer in spirit to <see cref="CraigslistSourceService"/>.
///
/// Guard-clause choice (documented per task instructions): if the Client isn't configured for
/// Telegram, or has no <see cref="Client.NotificationTarget"/>, this THROWS
/// <see cref="InvalidOperationException"/> rather than silently no-op/logging. Rationale:
/// mirrors the eBay-auth lesson already applied in this codebase (see
/// EbaySourceService.EnsureAccessTokenAsync remarks) - a misrouted/misconfigured notification
/// is a caller bug (whoever picks a sender for a Client picked the wrong one, or a Client was
/// saved without a target), and silently swallowing it would hide that behind what looks like
/// "notification successfully processed." Callers that route across multiple channel senders
/// (not built yet - see INotificationSender remarks) are expected to check
/// Client.NotificationChannel themselves before calling the matching sender, so this exception
/// should only ever fire on a real misconfiguration.
///
/// Non-2xx Telegram API responses also throw (not swallowed) for the same reason - same lesson
/// as the eBay auth-swallowing bug fixed in a prior task. Unlike the IListingSource
/// implementations (where a transient failure for one Watch shouldn't block others), a failed
/// notification for one Listing should surface loudly so it can be retried/alerted on, not
/// silently disappear.
/// </summary>
public class TelegramNotificationSender : INotificationSender
{
    private readonly HttpClient _httpClient;
    private readonly TelegramOptions _options;
    private readonly ILogger<TelegramNotificationSender> _logger;

    public TelegramNotificationSender(HttpClient httpClient, IOptions<TelegramOptions> options, ILogger<TelegramNotificationSender> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;
    }

    public async Task SendListingNotificationAsync(Client client, Listing listing, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(listing);

        if (client.NotificationChannel != NotificationChannel.Telegram)
        {
            throw new InvalidOperationException(
                $"TelegramNotificationSender cannot notify client {client.Id}: NotificationChannel is {client.NotificationChannel}, not Telegram.");
        }

        if (string.IsNullOrWhiteSpace(client.NotificationTarget))
        {
            throw new InvalidOperationException(
                $"TelegramNotificationSender cannot notify client {client.Id}: NotificationTarget (Telegram chat id) is missing.");
        }

        var requestUrl = $"{_options.ApiBaseUrl}{_options.BotToken}/sendMessage";
        var text = BuildMessageText(listing);

        var payload = new Dictionary<string, string>
        {
            ["chat_id"] = client.NotificationTarget,
            ["text"] = text,
            ["parse_mode"] = "HTML",
        };

        using var content = new FormUrlEncodedContent(payload);
        using var response = await _httpClient.PostAsync(requestUrl, content, cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            _logger.LogError(
                "Telegram sendMessage failed with status {StatusCode} for client {ClientId}: {Body}",
                response.StatusCode,
                client.Id,
                body);
            throw new InvalidOperationException(
                $"Telegram sendMessage failed: {(int)response.StatusCode} {response.ReasonPhrase}");
        }
    }

    /// <summary>
    /// Pure message-building logic (Title, Price if present, Url, Source), using Telegram's
    /// HTML parse_mode (see https://core.telegram.org/bots/api#html-style). Kept as a public
    /// static method (no HTTP required) so it can be unit tested directly, mirroring
    /// EbaySourceService.MapItemsToListings / CraigslistSourceService.MapFeedToListings.
    /// </summary>
    public static string BuildMessageText(Listing listing)
    {
        ArgumentNullException.ThrowIfNull(listing);

        var builder = new StringBuilder();
        builder.Append("<b>New listing match:</b> ").Append(EscapeHtml(listing.Title)).Append('\n');

        if (listing.Price.HasValue)
        {
            builder.Append("Price: $").Append(listing.Price.Value.ToString("0.00", CultureInfo.InvariantCulture)).Append('\n');
        }

        builder.Append("Source: ").Append(EscapeHtml(listing.Source.ToString())).Append('\n');
        builder.Append(EscapeHtml(listing.Url));

        return builder.ToString();
    }

    private static string EscapeHtml(string value)
    {
        return string.IsNullOrEmpty(value)
            ? string.Empty
            : value.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
    }
}
