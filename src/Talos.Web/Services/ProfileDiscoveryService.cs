using System.Text.RegularExpressions;
using Talos.Web.Models;
using Talos.Web.Services.IdentityProviders;

namespace Talos.Web.Services;

public class ProfileDiscoveryService(
    IHttpClientFactory httpClientFactory,
    IIdentityProviderFactory providerFactory,
    IMicroformatsService microformatsService,
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
            
            // Parse microformats using library
            var microformats = microformatsService.Parse(html, new Uri(result.ProfileUrl));

            // Discover rel="me" links
            var relMeLinks = microformats.RelMeLinks;
            
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

            // Discover IndieAuth endpoints (check HTTP headers first, then HTML)
            result.AuthorizationEndpoint = DiscoverEndpointFromHeaders(response, "authorization_endpoint") 
                                          ?? microformats.AuthorizationEndpoint;
            result.TokenEndpoint = DiscoverEndpointFromHeaders(response, "token_endpoint") 
                                  ?? microformats.TokenEndpoint;

            result.Success = true;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error discovering profile for {ProfileUrl}", profileUrl);
            result.Error = "An error occurred while discovering your profile.";
        }

        return result;
    }

    private static string? DiscoverEndpointFromHeaders(HttpResponseMessage response, string rel)
    {
        // Check HTTP Link header (RFC 8288)
        if (response.Headers.TryGetValues("Link", out var linkHeaders))
        {
            foreach (var header in linkHeaders)
            {
                var match = Regex.Match(
                    header, 
                    $"""<([^>]+)>;\s*rel="?{rel}"?""");
                if (match.Success)
                {
                    return match.Groups[1].Value;
                }
            }
        }
        return null;
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
