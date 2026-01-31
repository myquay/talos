namespace Talos.Web.Services;

/// <summary>
/// URL validation helpers for IndieAuth compliance
/// </summary>
public static class UrlValidator
{
    /// <summary>
    /// Validates that a URL is a valid HTTPS URL (or localhost for dev)
    /// </summary>
    public static bool IsValidHttpsUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return false;

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            return false;

        // Must be HTTPS (except localhost for development)
        if (uri.Scheme != "https" && uri.Host != "localhost" && uri.Host != "127.0.0.1")
            return false;

        // No fragments allowed per IndieAuth spec
        if (!string.IsNullOrEmpty(uri.Fragment))
            return false;

        // No userinfo allowed (username:password@)
        if (!string.IsNullOrEmpty(uri.UserInfo))
            return false;

        return true;
    }

    /// <summary>
    /// Validates a client_id URL per IndieAuth specification
    /// </summary>
    public static bool IsValidClientId(string? clientId)
    {
        if (!IsValidHttpsUrl(clientId))
            return false;

        var uri = new Uri(clientId!);

        // Must not be an IP address (except localhost)
        if (uri.HostNameType == UriHostNameType.IPv4 || uri.HostNameType == UriHostNameType.IPv6)
        {
            if (uri.Host != "127.0.0.1" && uri.Host != "::1" && uri.Host != "localhost")
                return false;
        }

        // Path must be non-empty (at least /)
        if (string.IsNullOrEmpty(uri.AbsolutePath) || uri.AbsolutePath == "")
            return false;

        return true;
    }

    /// <summary>
    /// Validates a redirect_uri matches the client_id per IndieAuth specification
    /// </summary>
    public static bool IsValidRedirectUri(string? redirectUri, string? clientId)
    {
        if (!IsValidHttpsUrl(redirectUri) || !IsValidClientId(clientId))
            return false;

        var redirectUriParsed = new Uri(redirectUri!);
        var clientIdParsed = new Uri(clientId!);

        // Redirect URI must be on the same host as client_id
        // or be a prefix path match
        if (!string.Equals(redirectUriParsed.Host, clientIdParsed.Host, StringComparison.OrdinalIgnoreCase))
            return false;

        // Must have same scheme
        if (redirectUriParsed.Scheme != clientIdParsed.Scheme)
            return false;

        // Port must match (if specified)
        if (redirectUriParsed.Port != clientIdParsed.Port)
            return false;

        return true;
    }

    /// <summary>
    /// Normalizes a profile URL per IndieAuth specification
    /// </summary>
    public static string NormalizeProfileUrl(string url)
    {
        // Add https:// if no scheme
        if (!url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) && 
            !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            url = "https://" + url;
        }

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            return url;

        // Normalize to lowercase host
        var normalized = uri.GetLeftPart(UriPartial.Authority).ToLowerInvariant();
        
        // Add path, defaulting to /
        var path = uri.AbsolutePath;
        if (string.IsNullOrEmpty(path))
            path = "/";
        
        normalized += path;

        // Remove trailing slash except for root
        if (normalized.EndsWith("/") && normalized.Count(c => c == '/') > 3)
            normalized = normalized.TrimEnd('/');

        return normalized;
    }
}

