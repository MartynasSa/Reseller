using Application.Domain.Entities;

namespace Application.Ports;

/// <summary>
/// Abstracts a marketplace source (eBay, Craigslist, Facebook Marketplace, OfferUp, etc.)
/// that can search for listings matching a Watch's search criteria.
/// </summary>
public interface IListingSource
{
    Task<IReadOnlyList<Listing>> SearchAsync(Watch watch, CancellationToken cancellationToken = default);
}
