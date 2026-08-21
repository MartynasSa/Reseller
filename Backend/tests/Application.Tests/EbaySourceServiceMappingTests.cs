using Application.Domain.Enums;
using Infrastructure.Services;

namespace Application.Tests;

public class EbaySourceServiceMappingTests
{
    [Fact]
    public void MapItemsToListings_MapsFieldsCorrectly_ForValidAndEdgeCaseItems()
    {
        var watchId = Guid.NewGuid();
        var response = new EbaySourceService.EbaySearchResponse
        {
            Total = 3,
            ItemSummaries = new List<EbaySourceService.EbayItemSummary>
            {
                new()
                {
                    ItemId = "v1|123456789|0",
                    Title = "Vintage Rolex Watch",
                    Price = new EbaySourceService.EbayPrice { Value = "199.99", Currency = "USD" },
                    ItemWebUrl = "https://www.ebay.com/itm/123456789",
                    ItemCreationDate = DateTimeOffset.Parse("2026-08-01T12:00:00.000Z")
                },
                new()
                {
                    ItemId = "v1|987654321|0",
                    Title = "Used Bicycle",
                    Price = new EbaySourceService.EbayPrice { Value = "45.00", Currency = "USD" },
                    ItemWebUrl = "https://www.ebay.com/itm/987654321",
                    ItemCreationDate = null
                },
                new()
                {
                    ItemId = "v1|555555555|0",
                    Title = "Item With Missing Price",
                    Price = null,
                    ItemWebUrl = "https://www.ebay.com/itm/555555555",
                    ItemCreationDate = null
                }
            }
        };

        var listings = EbaySourceService.MapItemsToListings(response, watchId);

        Assert.Equal(3, listings.Count);

        var first = listings[0];
        Assert.Equal("Vintage Rolex Watch", first.Title);
        Assert.Equal(199.99m, first.Price);
        Assert.Equal("https://www.ebay.com/itm/123456789", first.Url);
        Assert.Equal(ListingSource.eBay, first.Source);
        Assert.Equal(watchId, first.MatchedWatchId);
        Assert.Null(first.PostedAt);

        var second = listings[1];
        Assert.Equal("Used Bicycle", second.Title);
        Assert.Equal(45.00m, second.Price);
        Assert.Equal(ListingSource.eBay, second.Source);
        Assert.Equal(watchId, second.MatchedWatchId);
        Assert.Null(second.PostedAt);

        var third = listings[2];
        Assert.Equal("Item With Missing Price", third.Title);
        Assert.Null(third.Price);
        Assert.Equal(ListingSource.eBay, third.Source);
        Assert.Equal(watchId, third.MatchedWatchId);
        Assert.Null(third.PostedAt);
    }

    [Fact]
    public void MapItemsToListings_ReturnsEmptyList_WhenItemSummariesIsNull()
    {
        var watchId = Guid.NewGuid();
        var response = new EbaySourceService.EbaySearchResponse
        {
            Total = 0,
            ItemSummaries = null
        };

        var listings = EbaySourceService.MapItemsToListings(response, watchId);

        Assert.NotNull(listings);
        Assert.Empty(listings);
    }
}
