using Application.Domain.Entities;

namespace Application.Ports;

/// <summary>
/// Pure rule-based filtering/deduplication stage that runs after an <see cref="IListingSource"/>
/// produces raw candidate <see cref="Listing"/>s and before a Listing is considered "new" and
/// eligible to trigger a notification. No AI/ML - simple sanity checks + normalized-key dedup
/// (see <see cref="Services.ListingFilterService"/> for the exact rules).
///
/// Deliberately a pure, DB-free function: sourcing the "already seen" listings for a watch
/// (e.g. querying <c>DbSet&lt;Listing&gt;</c> by MatchedWatchId) is a separate, thin concern
/// left to the caller (a future polling worker), so this filtering logic itself stays trivially
/// unit-testable without a DbContext.
/// </summary>
public interface IListingFilterService
{
    /// <summary>
    /// Applies sanity checks and deduplication to <paramref name="candidates"/> (freshly
    /// fetched from one or more sources), treating any listing whose dedup key matches one in
    /// <paramref name="alreadySeen"/> (e.g. previously stored listings for the same Watch) as a
    /// duplicate. Returns the subset that is eligible to notify, in a deterministic order.
    /// </summary>
    /// <param name="candidates">Freshly fetched candidate listings to filter, from one or more sources.</param>
    /// <param name="alreadySeen">Previously stored/notified listings (e.g. for the same Watch) used only as a dedup reference set - never returned themselves.</param>
    /// <param name="watch">The Watch the candidates were matched against; supplies Location for the dedup key.</param>
    IReadOnlyList<Listing> FilterAndDeduplicate(
        IEnumerable<Listing> candidates,
        IEnumerable<Listing> alreadySeen,
        Watch watch);
}
