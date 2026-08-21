using Application.Domain.Enums;
using Infrastructure.Services;

namespace Application.Tests;

public class CraigslistSourceServiceMappingTests
{
    private const string SampleRss = """
        <?xml version="1.0" encoding="UTF-8"?>
        <rss version="2.0" xmlns:atom="http://www.w3.org/2005/Atom">
          <channel>
            <title>craigslist | electronics for sale</title>
            <link>https://newyork.craigslist.org/search/ela?query=iphone</link>
            <item>
              <title>$50 - iPhone 12 64GB Unlocked</title>
              <link>https://newyork.craigslist.org/mnh/ele/d/new-york-iphone-12-64gb/1234567890.html</link>
              <pubDate>Fri, 21 Aug 2026 10:15:00 -0400</pubDate>
              <description>iPhone 12 in great condition</description>
            </item>
            <item>
              <title>$1,299.99 - MacBook Pro 16" M3</title>
              <link>https://newyork.craigslist.org/mnh/ele/d/new-york-macbook-pro/1234567891.html</link>
              <pubDate>Fri, 21 Aug 2026 09:00:00 -0400</pubDate>
              <description>Barely used MacBook Pro</description>
            </item>
            <item>
              <title>iPhone 11 case bundle (no price listed)</title>
              <link>https://newyork.craigslist.org/mnh/ele/d/new-york-iphone-case/1234567892.html</link>
              <pubDate>Fri, 21 Aug 2026 08:30:00 -0400</pubDate>
              <description>Assorted cases</description>
            </item>
          </channel>
        </rss>
        """;

    [Fact]
    public void MapFeedToListings_MapsFieldsCorrectly_ForValidAndEdgeCaseItems()
    {
        var watchId = Guid.NewGuid();

        var listings = CraigslistSourceService.MapFeedToListings(SampleRss, watchId);

        Assert.Equal(3, listings.Count);

        var first = listings[0];
        Assert.Equal("$50 - iPhone 12 64GB Unlocked", first.Title);
        Assert.Equal(50m, first.Price);
        Assert.Equal("https://newyork.craigslist.org/mnh/ele/d/new-york-iphone-12-64gb/1234567890.html", first.Url);
        Assert.Equal(ListingSource.Craigslist, first.Source);
        Assert.Equal(watchId, first.MatchedWatchId);
        Assert.NotNull(first.PostedAt);

        var second = listings[1];
        Assert.Equal(1299.99m, second.Price);
        Assert.Equal(ListingSource.Craigslist, second.Source);

        var third = listings[2];
        Assert.Equal("iPhone 11 case bundle (no price listed)", third.Title);
        Assert.Null(third.Price);
        Assert.Equal(ListingSource.Craigslist, third.Source);
        Assert.Equal(watchId, third.MatchedWatchId);
    }

    [Fact]
    public void MapFeedToListings_ReturnsEmptyList_WhenNoItems()
    {
        const string emptyRss = """
            <?xml version="1.0" encoding="UTF-8"?>
            <rss version="2.0"><channel><title>craigslist</title></channel></rss>
            """;

        var listings = CraigslistSourceService.MapFeedToListings(emptyRss, Guid.NewGuid());

        Assert.NotNull(listings);
        Assert.Empty(listings);
    }

    [Fact]
    public void MapFeedToListings_ReturnsEmptyList_ForBlankInput()
    {
        var listings = CraigslistSourceService.MapFeedToListings(string.Empty, Guid.NewGuid());

        Assert.Empty(listings);
    }

    [Fact]
    public void MapFeedToListings_ReturnsEmptyList_ForMalformedXml()
    {
        var listings = CraigslistSourceService.MapFeedToListings("<rss><channel><item><title>oops</channel>", Guid.NewGuid());

        Assert.Empty(listings);
    }

    [Theory]
    [InlineData("$50 - iPhone 12 64GB", 50.0)]
    [InlineData("$1,299.99 - MacBook Pro", 1299.99)]
    [InlineData("$0 - Free desk", 0.0)]
    [InlineData("iPhone case bundle", null)]
    [InlineData("", null)]
    public void ParseLeadingPrice_ParsesLeadingDollarAmount(string title, double? expected)
    {
        var result = CraigslistSourceService.ParseLeadingPrice(title);

        if (expected is null)
        {
            Assert.Null(result);
        }
        else
        {
            Assert.Equal((decimal)expected.Value, result);
        }
    }

    [Theory]
    [InlineData("newyork", "newyork")]
    [InlineData("  New York  ", "newyork")]
    [InlineData("LOS ANGELES", "losangeles")]
    [InlineData("", "")]
    [InlineData(null, "")]
    public void NormalizeLocationToCitySlug_LowercasesAndStripsWhitespace(string? location, string expected)
    {
        var result = CraigslistSourceService.NormalizeLocationToCitySlug(location);

        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("electronics", "ela")]
    [InlineData("Cell Phones", "moa")]
    [InlineData("computers", "sya")]
    [InlineData("unknown-category", "sss")]
    [InlineData("", "sss")]
    [InlineData(null, "sss")]
    public void MapCategoryToCode_MapsKnownCategories_FallsBackToAllForSale(string? category, string expected)
    {
        var result = CraigslistSourceService.MapCategoryToCode(category);

        Assert.Equal(expected, result);
    }
}
