using Microformats;

namespace Talos.Web.Services;

/// <summary>
/// Service for parsing microformats data from HTML content using the Microformats library.
/// </summary>
public class MicroformatsService(ILogger<MicroformatsService> logger) : IMicroformatsService
{
    /// <inheritdoc />
    public MicroformatsResult Parse(string html, Uri baseUrl)
    {
        var result = new MicroformatsResult();

        try
        {
            var parser = new Mf2().WithOptions(o =>
            {
                o.BaseUri = baseUrl;
                return o;
            });
            var parsed = parser.Parse(html);

            // Extract rel="me" links (resolve relative URLs)
            if (parsed.Rels.TryGetValue("me", out var meLinks))
            {
                result.RelMeLinks = meLinks
                    .Select(link => ResolveUrl(link, baseUrl))
                    .Where(url => url != null)
                    .Cast<string>()
                    .Distinct()
                    .ToList();
            }

            // Extract IndieAuth endpoints (resolve relative URLs)
            result.AuthorizationEndpoint = GetFirstResolvedUrl(parsed.Rels, "authorization_endpoint", baseUrl);
            result.TokenEndpoint = GetFirstResolvedUrl(parsed.Rels, "token_endpoint", baseUrl);
            result.IndieAuthMetadata = GetFirstResolvedUrl(parsed.Rels, "indieauth-metadata", baseUrl);
            result.Micropub = GetFirstResolvedUrl(parsed.Rels, "micropub", baseUrl);
            result.Microsub = GetFirstResolvedUrl(parsed.Rels, "microsub", baseUrl);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to parse microformats from {BaseUrl}", baseUrl);
        }

        return result;
    }

    private static string? GetFirstResolvedUrl(IDictionary<string, string[]> rels, string key, Uri baseUrl)
    {
        if (!rels.TryGetValue(key, out var urls) || urls.Length == 0)
            return null;

        return ResolveUrl(urls[0], baseUrl);
    }

    private static string? ResolveUrl(string url, Uri baseUrl)
    {
        if (string.IsNullOrWhiteSpace(url))
            return null;

        // Check if it's a proper HTTP(S) absolute URL
        if (Uri.TryCreate(url, UriKind.Absolute, out var absoluteUri) && 
            (absoluteUri.Scheme == "http" || absoluteUri.Scheme == "https"))
        {
            return absoluteUri.ToString();
        }

        // Resolve relative URL against base
        if (Uri.TryCreate(baseUrl, url, out var resolvedUri))
            return resolvedUri.ToString();

        return null;
    }
}
