namespace Application.Models.Craigslist;

/// <summary>
/// Configuration for polling Craigslist's public per-search RSS feeds. No API credentials
/// are needed (unlike eBay's OAuth flow) since these feeds are publicly documented and
/// require no auth - this is intentionally much smaller than <c>EbayOptions</c>.
/// </summary>
public class CraigslistOptions
{
    // {0} = city subdomain (e.g. "newyork"), {1} = category code (e.g. "ela"), {2} = URL-encoded query.
    public string SearchUrlTemplate { get; set; } = "https://{0}.craigslist.org/search/{1}?format=rss&query={2}";

    public int RequestTimeoutSeconds { get; set; } = 15;
}
