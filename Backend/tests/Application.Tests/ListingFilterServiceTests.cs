using Application.Domain.Entities;
using Application.Domain.Enums;
using Application.Services;

namespace Application.Tests;

public class ListingFilterServiceTests
{
    private static Watch MakeWatch(string location = "newyork") => new()
    {
        Id = Guid.NewGuid(),
        Location = location,
    };

    private static Listing MakeListing(
        string title = "iPhone 12 64GB",
        decimal? price = 50m,
        string url = "https://example.com/listing/1",
        ListingSource source = ListingSource.Craigslist,
        Guid? watchId = null) => new()
    {
        Title = title,
        Price = price,
        Url = url,
        Source = source,
        MatchedWatchId = watchId ?? Guid.NewGuid(),
        CreatedAt = DateTime.UtcNow,
    };

    private readonly ListingFilterService _sut = new();

    [Fact]
    public void FilterAndDeduplicate_RejectsListing_WithEmptyTitle()
    {
        var watch = MakeWatch();
        var candidates = new[] { MakeListing(title: "") };

        var result = _sut.FilterAndDeduplicate(candidates, Array.Empty<Listing>(), watch);

        Assert.Empty(result);
    }

    [Fact]
    public void FilterAndDeduplicate_RejectsListing_WithWhitespaceOnlyTitle()
    {
        var watch = MakeWatch();
        var candidates = new[] { MakeListing(title: "   ") };

        var result = _sut.FilterAndDeduplicate(candidates, Array.Empty<Listing>(), watch);

        Assert.Empty(result);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-10)]
    public void FilterAndDeduplicate_RejectsListing_WithZeroOrNegativePrice(decimal price)
    {
        var watch = MakeWatch();
        var candidates = new[] { MakeListing(price: price) };

        var result = _sut.FilterAndDeduplicate(candidates, Array.Empty<Listing>(), watch);

        Assert.Empty(result);
    }

    [Fact]
    public void FilterAndDeduplicate_AllowsListing_WithNullPrice()
    {
        var watch = MakeWatch();
        var candidates = new[] { MakeListing(price: null) };

        var result = _sut.FilterAndDeduplicate(candidates, Array.Empty<Listing>(), watch);

        Assert.Single(result);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not-a-url")]
    [InlineData("ftp://example.com/x")]
    public void FilterAndDeduplicate_RejectsListing_WithInvalidUrl(string url)
    {
        var watch = MakeWatch();
        var candidates = new[] { MakeListing(url: url) };

        var result = _sut.FilterAndDeduplicate(candidates, Array.Empty<Listing>(), watch);

        Assert.Empty(result);
    }

    [Fact]
    public void FilterAndDeduplicate_DedupsWithinBatch_KeepingFirstSeen_WhenSameSource()
    {
        var watch = MakeWatch();
        var first = MakeListing(title: "iPhone 12 64GB", price: 50m, url: "https://example.com/a", source: ListingSource.Craigslist);
        var second = MakeListing(title: "iPhone 12 64GB", price: 50m, url: "https://example.com/b", source: ListingSource.Craigslist);

        var result = _sut.FilterAndDeduplicate(new[] { first, second }, Array.Empty<Listing>(), watch);

        var single = Assert.Single(result);
        Assert.Equal(first.Url, single.Url);
    }

    [Fact]
    public void FilterAndDeduplicate_DedupsAgainstAlreadySeen()
    {
        var watch = MakeWatch();
        var alreadySeen = new[] { MakeListing(title: "iPhone 12 64GB", price: 50m, source: ListingSource.Craigslist) };
        var candidate = MakeListing(title: "iPhone 12 64GB", price: 50m, source: ListingSource.eBay);

        var result = _sut.FilterAndDeduplicate(new[] { candidate }, alreadySeen, watch);

        Assert.Empty(result);
    }

    [Fact]
    public void FilterAndDeduplicate_TitleNormalization_IsCaseAndWhitespaceInsensitive()
    {
        var watch = MakeWatch();
        var first = MakeListing(title: "  iPhone   12 (64GB)!!  ", price: 50m, url: "https://example.com/a");
        var second = MakeListing(title: "iphone 12 64gb", price: 50m, url: "https://example.com/b");

        var result = _sut.FilterAndDeduplicate(new[] { first, second }, Array.Empty<Listing>(), watch);

        Assert.Single(result);
    }

    [Fact]
    public void FilterAndDeduplicate_DoesNotTreatDifferentPrices_AsDuplicates()
    {
        var watch = MakeWatch();
        var first = MakeListing(title: "iPhone 12 64GB", price: 50m, url: "https://example.com/a");
        var second = MakeListing(title: "iPhone 12 64GB", price: 75m, url: "https://example.com/b");

        var result = _sut.FilterAndDeduplicate(new[] { first, second }, Array.Empty<Listing>(), watch);

        Assert.Equal(2, result.Count);
    }

    [Fact]
    public void FilterAndDeduplicate_DoesNotTreatDifferentLocations_AsDuplicates()
    {
        var first = MakeListing(title: "iPhone 12 64GB", price: 50m, url: "https://example.com/a");
        var second = MakeListing(title: "iPhone 12 64GB", price: 50m, url: "https://example.com/b");

        var resultNy = _sut.FilterAndDeduplicate(new[] { first }, Array.Empty<Listing>(), MakeWatch("newyork"));
        var resultLa = _sut.FilterAndDeduplicate(new[] { second }, Array.Empty<Listing>(), MakeWatch("losangeles"));

        Assert.Single(resultNy);
        Assert.Single(resultLa);
    }

    [Fact]
    public void FilterAndDeduplicate_PrefersEbay_OverCraigslist_OnDuplicateKey()
    {
        var watch = MakeWatch();
        var craigslist = MakeListing(title: "iPhone 12 64GB", price: 50m, url: "https://craigslist.example/x", source: ListingSource.Craigslist);
        var ebay = MakeListing(title: "iPhone 12 64GB", price: 50m, url: "https://ebay.example/y", source: ListingSource.eBay);

        var result = _sut.FilterAndDeduplicate(new[] { craigslist, ebay }, Array.Empty<Listing>(), watch);

        var single = Assert.Single(result);
        Assert.Equal(ListingSource.eBay, single.Source);
        Assert.Equal(ebay.Url, single.Url);
    }

    [Fact]
    public void FilterAndDeduplicate_PrefersEbay_OverCraigslist_RegardlessOfOrder()
    {
        var watch = MakeWatch();
        var ebay = MakeListing(title: "iPhone 12 64GB", price: 50m, url: "https://ebay.example/y", source: ListingSource.eBay);
        var craigslist = MakeListing(title: "iPhone 12 64GB", price: 50m, url: "https://craigslist.example/x", source: ListingSource.Craigslist);

        // eBay listed first this time - result should still be eBay.
        var result = _sut.FilterAndDeduplicate(new[] { ebay, craigslist }, Array.Empty<Listing>(), watch);

        var single = Assert.Single(result);
        Assert.Equal(ListingSource.eBay, single.Source);
    }

    [Fact]
    public void FilterAndDeduplicate_ReturnsEmpty_WhenNoCandidates()
    {
        var watch = MakeWatch();

        var result = _sut.FilterAndDeduplicate(Array.Empty<Listing>(), Array.Empty<Listing>(), watch);

        Assert.Empty(result);
    }

    [Theory]
    [InlineData("  iPhone   12 (64GB)!!  ", "iphone 12 64gb")]
    [InlineData("MacBook Pro, 16\"", "macbook pro 16")]
    [InlineData("", "")]
    [InlineData(null, "")]
    public void NormalizeTitle_LowercasesTrimsAndStripsPunctuation(string? title, string expected)
    {
        var result = ListingFilterService.NormalizeTitle(title);

        Assert.Equal(expected, result);
    }
}
