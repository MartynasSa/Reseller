namespace Application.Models.Telegram;

/// <summary>
/// Configuration for sending notifications via the Telegram Bot API. Sending a message is a
/// simple unauthenticated-transport HTTP POST (the token IS the auth, carried in the URL path)
/// to <c>{ApiBaseUrl}{BotToken}/sendMessage</c> - no OAuth flow, so this is much closer to
/// <c>CraigslistOptions</c>'s simplicity than <c>EbayOptions</c>'s client-credentials dance.
/// </summary>
public class TelegramOptions
{
    // Intentionally empty by default - no real bot token exists yet in any environment. Must
    // be supplied via configuration/secrets before TelegramNotificationSender can make live calls.
    public string BotToken { get; set; } = string.Empty;

    public string ApiBaseUrl { get; set; } = "https://api.telegram.org/bot";
}
