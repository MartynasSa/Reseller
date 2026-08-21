namespace Application.Models.Ebay;

public class EbayOptions
{
    public string ClientId { get; set; } = string.Empty;
    public string ClientSecret { get; set; } = string.Empty;
    public string OAuthTokenUrl { get; set; } = "https://api.ebay.com/identity/v1/oauth2/token";
    public string BrowseApiBaseUrl { get; set; } = "https://api.ebay.com/buy/browse/v1";
    public string MarketplaceId { get; set; } = "EBAY_US";
}
