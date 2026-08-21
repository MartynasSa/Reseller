namespace Application.Domain.Entities;

public class Watch
{
    public Guid Id { get; set; }

    public Guid ClientId { get; set; }
    public Client? Client { get; set; }

    public string Keywords { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string Location { get; set; } = string.Empty;
    public decimal? MinPrice { get; set; }
    public decimal? MaxPrice { get; set; }

    // Comma-delimited list of marketplace names (e.g. "eBay,Craigslist"). Kept as a simple
    // delimited string rather than an array/JSON column so it stays trivially portable
    // across providers and easy to query/index with plain EF Core string operators.
    public string SourceMarketplaces { get; set; } = string.Empty;

    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; }

    public ICollection<Listing> Listings { get; set; } = new List<Listing>();
}
