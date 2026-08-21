using Application.Domain.Entities;
using Application.Domain.Enums;
using Infrastructure.Services;

namespace Application.Tests;

public class TelegramNotificationSenderTests
{
    private static Listing SampleListing(decimal? price = 50m) => new()
    {
        Id = Guid.NewGuid(),
        Title = "iPhone 12 64GB Unlocked",
        Price = price,
        Url = "https://newyork.craigslist.org/mnh/ele/d/new-york-iphone-12-64gb/1234567890.html",
        Source = ListingSource.Craigslist,
        MatchedWatchId = Guid.NewGuid(),
        CreatedAt = DateTime.UtcNow,
    };

    [Fact]
    public void BuildMessageText_IncludesTitlePriceSourceAndUrl_WhenPricePresent()
    {
        var listing = SampleListing(price: 1299.99m);

        var text = TelegramNotificationSender.BuildMessageText(listing);

        Assert.Contains("iPhone 12 64GB Unlocked", text);
        Assert.Contains("1299.99", text);
        Assert.Contains("Craigslist", text);
        Assert.Contains(listing.Url, text);
    }

    [Fact]
    public void BuildMessageText_OmitsPriceLine_WhenPriceIsNull()
    {
        var listing = SampleListing(price: null);

        var text = TelegramNotificationSender.BuildMessageText(listing);

        Assert.DoesNotContain("Price:", text);
        Assert.Contains(listing.Title, text);
        Assert.Contains(listing.Url, text);
    }

    [Fact]
    public void BuildMessageText_EscapesHtmlSpecialCharacters_InTitle()
    {
        var listing = SampleListing();
        listing.Title = "iPhone <mint> & \"like new\"";

        var text = TelegramNotificationSender.BuildMessageText(listing);

        Assert.DoesNotContain("<mint>", text);
        Assert.Contains("&lt;mint&gt;", text);
        Assert.Contains("&amp;", text);
    }

    [Fact]
    public async Task SendListingNotificationAsync_Throws_WhenClientChannelIsNotTelegram()
    {
        var sender = new TelegramNotificationSender(new HttpClient(), FakeOptions(), Microsoft.Extensions.Logging.Abstractions.NullLogger<TelegramNotificationSender>.Instance);
        var client = new Client
        {
            Id = Guid.NewGuid(),
            NotificationChannel = NotificationChannel.Email,
            NotificationTarget = "123456",
        };

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => sender.SendListingNotificationAsync(client, SampleListing()));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task SendListingNotificationAsync_Throws_WhenNotificationTargetIsMissing(string? target)
    {
        var sender = new TelegramNotificationSender(new HttpClient(), FakeOptions(), Microsoft.Extensions.Logging.Abstractions.NullLogger<TelegramNotificationSender>.Instance);
        var client = new Client
        {
            Id = Guid.NewGuid(),
            NotificationChannel = NotificationChannel.Telegram,
            NotificationTarget = target,
        };

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => sender.SendListingNotificationAsync(client, SampleListing()));
    }

    private static Microsoft.Extensions.Options.IOptions<Application.Models.Telegram.TelegramOptions> FakeOptions()
    {
        return Microsoft.Extensions.Options.Options.Create(new Application.Models.Telegram.TelegramOptions
        {
            BotToken = "test-token",
            ApiBaseUrl = "https://api.telegram.org/bot",
        });
    }
}
