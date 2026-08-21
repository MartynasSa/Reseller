using System.Text;
using Application.Domain.Entities;
using Application.Domain.Enums;
using Application.Ports;

namespace Application.Services;

/// <summary>
/// Rule-based (no AI/ML) implementation of <see cref="IListingFilterService"/> for v0.
///
/// Sanity checks (a listing failing any of these is dropped, never reaches dedup):
///   - Title must be non-empty after trimming.
///   - Url must be non-empty after trimming (cheap "looks like a URL" check - starts with
///     "http://" or "https://" - full URI validation isn't warranted for v0).
///   - Price, if present, must be &gt; 0. Null Price is allowed ("no price listed" is not spam).
///
/// Dedup key: normalized Title + Price + Watch.Location. Title normalization is lowercase,
/// trim, collapse internal whitespace, and strip punctuation, so "  iPhone 12 (64GB)!!" and
/// "iphone 12 64gb" collapse to the same key. Price is included as-is (null vs a specific
/// value are different keys - e.g. "no price listed" is not a dup of "listed at $50").
/// Location comes from the Watch rather than the Listing (Listing has no Location field) - so
/// the key is really "this Watch would consider these the same real-world item".
///
/// Dedup scope: within the candidate batch AND against `alreadySeen`. Tie-break when multiple
/// candidates share a dedup key: prefer eBay over Craigslist (eBay listings tend to have more
/// reliable structured price/title data - see EbaySourceService vs CraigslistSourceService's
/// regex-based price parsing), otherwise keep first-seen (stable, order-preserving) among
/// same-source duplicates. Listings already in `alreadySeen` always win over any candidate
/// with the same key, regardless of source, since the point is "don't notify again."
/// </summary>
public class ListingFilterService : IListingFilterService
{
    public IReadOnlyList<Listing> FilterAndDeduplicate(
        IEnumerable<Listing> candidates,
        IEnumerable<Listing> alreadySeen,
        Watch watch)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        ArgumentNullException.ThrowIfNull(alreadySeen);
        ArgumentNullException.ThrowIfNull(watch);

        var seenKeys = new HashSet<string>(
            alreadySeen.Where(PassesSanityChecks).Select(l => BuildDedupKey(l, watch.Location)));

        // Preserve first-seen order of the input; only the "which candidate wins a duplicate
        // key" decision uses source priority, not the overall output ordering.
        var winners = new Dictionary<string, Listing>();
        var order = new List<string>();

        foreach (var candidate in candidates)
        {
            if (!PassesSanityChecks(candidate))
            {
                continue;
            }

            var key = BuildDedupKey(candidate, watch.Location);

            if (seenKeys.Contains(key))
            {
                // Already stored/notified for this watch - never re-notify.
                continue;
            }

            if (winners.TryGetValue(key, out var existing))
            {
                if (IsHigherPriority(candidate, existing))
                {
                    winners[key] = candidate;
                }
                // else: keep the existing (first-seen or higher-priority) winner.
            }
            else
            {
                winners[key] = candidate;
                order.Add(key);
            }
        }

        return order.Select(key => winners[key]).ToList();
    }

    /// <summary>
    /// Title non-empty, Url non-empty and URL-shaped, Price null or &gt; 0.
    /// </summary>
    private static bool PassesSanityChecks(Listing listing)
    {
        if (listing is null)
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(listing.Title))
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(listing.Url) ||
            !(listing.Url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
              listing.Url.StartsWith("https://", StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        if (listing.Price.HasValue && listing.Price.Value <= 0)
        {
            return false;
        }

        return true;
    }

    /// <summary>
    /// eBay is preferred over Craigslist on a tie (see class remarks); other sources fall back
    /// to "keep first-seen" (i.e. never treated as higher priority than the existing winner).
    /// </summary>
    private static bool IsHigherPriority(Listing candidate, Listing existing)
    {
        return candidate.Source == ListingSource.eBay && existing.Source != ListingSource.eBay;
    }

    public static string BuildDedupKey(Listing listing, string? location)
    {
        var normalizedTitle = NormalizeTitle(listing.Title);
        var priceKey = listing.Price.HasValue ? listing.Price.Value.ToString("0.00") : "null";
        var normalizedLocation = string.IsNullOrWhiteSpace(location)
            ? string.Empty
            : location.Trim().ToLowerInvariant();

        return $"{normalizedTitle}|{priceKey}|{normalizedLocation}";
    }

    /// <summary>
    /// Lowercase, trim, strip punctuation, collapse internal whitespace to single spaces.
    /// </summary>
    public static string NormalizeTitle(string? title)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(title.Length);
        foreach (var c in title.ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(c) || char.IsWhiteSpace(c))
            {
                builder.Append(c);
            }
            // else: punctuation/symbols are stripped entirely (not replaced with a space).
        }

        var collapsed = string.Join(' ', builder.ToString().Split(
            (char[]?)null, StringSplitOptions.RemoveEmptyEntries));

        return collapsed;
    }
}
