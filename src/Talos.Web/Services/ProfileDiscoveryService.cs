using HtmlAgilityPack;
using Talos.Web.Models;
using Talos.Web.Services.IdentityProviders;

namespace Talos.Web.Services;

public class ProfileDiscoveryService(
    IHttpClientFactory httpClientFactory,
    IIdentityProviderFactory providerFactory,
    ILogger<ProfileDiscoveryService> logger)
    : IProfileDiscoveryService
{
    public async Task<ProfileDiscoveryResult> DiscoverProfileAsync(string profileUrl)
    {
        var result = new ProfileDiscoveryResult
        {
            ProfileUrl = NormalizeProfileUrl(profileUrl)
        };

        try
        {
            var client = httpClientFactory.CreateClient("ProfileDiscovery");
            var response = await client.GetAsync(result.ProfileUrl);
            
            if (!response.IsSuccessStatusCode)
            {
                result.Error = $"Failed to fetch profile: {response.StatusCode}";
                return result;
            }

            var html = await response.Content.ReadAsStringAsync();
            var doc = new HtmlDocument();
            doc.LoadHtml(html);

            // Discover rel="me" links
            var relMeLinks = DiscoverRelMeLinks(doc, result.ProfileUrl);
            
            // Match links against supported identity providers
            foreach (var link in relMeLinks)
            {
                var provider = providerFactory.GetProviderForUrl(link);
                if (provider != null)
                {
                    result.Providers.Add(new DiscoveredProvider
                    {
                        Type = provider.ProviderType,
                        Name = provider.DisplayName,
                        ProfileUrl = link
                    });
                }
            }

            // Discover IndieAuth endpoints
            result.AuthorizationEndpoint = DiscoverEndpoint(doc, response, "authorization_endpoint");
            result.TokenEndpoint = DiscoverEndpoint(doc, response, "token_endpoint");

            result.Success = true;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error discovering profile for {ProfileUrl}", profileUrl);
            result.Error = "An error occurred while discovering your profile.";
        }

        return result;
    }

    private static List<string> DiscoverRelMeLinks(HtmlDocument doc, string baseUrl)
    {
        var links = new List<string>();
        var baseUri = new Uri(baseUrl);

        // Find all <a rel="me"> and <link rel="me"> elements
        var relMeNodes = doc.DocumentNode.SelectNodes("//a[@rel='me']|//link[@rel='me']");

        foreach (var node in relMeNodes)
        {
            var href = node.GetAttributeValue("href", "");
            if (string.IsNullOrWhiteSpace(href))
                continue;

            // Resolve relative URLs
            if (Uri.TryCreate(baseUri, href, out var absoluteUri))
            {
                links.Add(absoluteUri.ToString());
            }
        }

        return links.Distinct().ToList();
    }

    private static string? DiscoverEndpoint(HtmlDocument doc, HttpResponseMessage response, string rel)
    {
        // Check HTTP Link header first
        if (response.Headers.TryGetValues("Link", out var linkHeaders))
        {
            foreach (var header in linkHeaders)
            {
                var match = System.Text.RegularExpressions.Regex.Match(
                    header, 
                    $"""<([^>]+)>;\s*rel="?{rel}"?""");
                if (match.Success)
                {
                    return match.Groups[1].Value;
                }
            }
        }

        // Check HTML <link> elements
        var linkNode = doc.DocumentNode.SelectSingleNode($"//link[@rel='{rel}']");
        var href = linkNode.GetAttributeValue("href", string.Empty);
        return string.IsNullOrEmpty(href) ? null : href;
    }

    private static string NormalizeProfileUrl(string url)
    {
        // Ensure URL has scheme
        if (!url.StartsWith("http://") && !url.StartsWith("https://"))
        {
            url = "https://" + url;
        }

        // Ensure trailing slash for domain-only URLs
        var uri = new Uri(url);
        if (string.IsNullOrEmpty(uri.AbsolutePath) || uri.AbsolutePath == "/")
        {
            url = uri.GetLeftPart(UriPartial.Authority) + "/";
        }

        return url.ToLowerInvariant();
    }
}

