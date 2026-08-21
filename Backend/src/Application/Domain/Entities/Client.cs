using Application.Domain.Enums;

namespace Application.Domain.Entities;

public class Client
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public NotificationChannel NotificationChannel { get; set; } = NotificationChannel.Telegram;

    // Channel-specific identifier (e.g. Telegram chat id) used to actually deliver alerts.
    public string? NotificationTarget { get; set; }

    public DateTime CreatedAt { get; set; }

    public ICollection<Watch> Watches { get; set; } = new List<Watch>();
}
