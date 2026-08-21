using Application.Domain.Entities;

namespace Application.Ports;

/// <summary>
/// Abstracts delivery of a "new matching Listing" notification to a Client over a specific
/// channel (Telegram, and eventually Email - see <see cref="Domain.Enums.NotificationChannel"/>).
///
/// This is deliberately a per-channel sender, not a router: each implementation (e.g.
/// <c>TelegramNotificationSender</c>) only knows how to deliver over its own channel and is
/// expected to guard against being called for a Client configured for a different channel.
/// Picking the right sender for a given Client's <see cref="Client.NotificationChannel"/> (a
/// dispatcher across multiple registered senders) is out of scope here and is left for when a
/// second channel (Email) is actually added.
/// </summary>
public interface INotificationSender
{
    Task SendListingNotificationAsync(Client client, Listing listing, CancellationToken cancellationToken = default);
}
