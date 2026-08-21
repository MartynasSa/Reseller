using Application.Domain.Enums;

namespace Application.Domain.Entities;

public class Listing
{
    public Guid Id { get; set; }

    public string Title { get; set; } = string.Empty;
    public decimal? Price { get; set; }
    public string Url { get; set; } = string.Empty;
    public ListingSource Source { get; set; }
    public DateTimeOffset? PostedAt { get; set; }

    public Guid MatchedWatchId { get; set; }
    public Watch? MatchedWatch { get; set; }

    // When we ingested/matched this listing, for our own ordering (distinct from PostedAt,
    // which reflects the marketplace's own listing time and may be unavailable).
    public DateTime CreatedAt { get; set; }
}
