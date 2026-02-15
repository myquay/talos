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

        // Must not contain dot-segments per IndieAuth spec §3.3
        if (HasDotSegments(clientId!))
            return false;

        return true;
    }

    /// <summary>
    /// Validates a profile URL per IndieAuth specification §3.2.
    /// Profile URLs have stricter requirements than client IDs:
    /// no port allowed, no IP addresses (not even loopback).
    /// </summary>
    public static bool IsValidProfileUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return false;

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            return false;

        // ID-2: Must be https or http scheme
        if (uri.Scheme != "https" && uri.Scheme != "http")
            return false;

        // ID-3: Must contain a path component (at least /)
        if (string.IsNullOrEmpty(uri.AbsolutePath))
            return false;

        // ID-4: Must not contain dot-segments
        if (HasDotSegments(url))
            return false;

        // ID-5: Must not contain a fragment
        if (!string.IsNullOrEmpty(uri.Fragment))
            return false;

        // ID-6: Must not contain username or password
        if (!string.IsNullOrEmpty(uri.UserInfo))
            return false;

        // ID-7: Must not contain a port
        if (!uri.IsDefaultPort)
            return false;

        // ID-8: Host must be a domain name, not an IP address (no loopback exception)
        if (uri.HostNameType == UriHostNameType.IPv4 ||
            uri.HostNameType == UriHostNameType.IPv6)
            return false;

        return true;
    }

    /// <summary>
    /// Validates a redirect_uri matches the client_id per IndieAuth specification.
    /// Returns true only for same-origin redirect URIs. Cross-origin redirect URIs
    /// require client metadata verification (not yet implemented — see GAP-4).
    /// </summary>
    public static bool IsValidRedirectUri(string? redirectUri, string? clientId)
    {
        if (string.IsNullOrWhiteSpace(redirectUri))
            return false;

        // Block dangerous schemes before any further parsing
        if (HasDangerousScheme(redirectUri))
            return false;

        if (!IsValidHttpsUrl(redirectUri) || !IsValidClientId(clientId))
            return false;

        var redirectUriParsed = new Uri(redirectUri!);
        var clientIdParsed = new Uri(clientId!);

        // Reject dot-segments in path (path traversal) — check raw string, not parsed Uri
        if (HasDotSegments(redirectUri!))
            return false;

        // Redirect URI must be on the same host as client_id
        // Cross-origin redirect URIs are rejected until client metadata fetching is implemented (GAP-4)
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
    /// Checks whether a URL string starts with a dangerous scheme (javascript:, data:, etc.)
    /// This check is performed before URI parsing to catch schemes that Uri.TryCreate may accept.
    /// </summary>
    public static bool HasDangerousScheme(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return false;

        var trimmed = url.TrimStart();
        return trimmed.StartsWith("javascript:", StringComparison.OrdinalIgnoreCase)
            || trimmed.StartsWith("data:", StringComparison.OrdinalIgnoreCase)
            || trimmed.StartsWith("vbscript:", StringComparison.OrdinalIgnoreCase)
            || trimmed.StartsWith("file:", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Checks whether a URL string contains single-dot or double-dot path segments
    /// which are not allowed per IndieAuth spec §3.3.
    /// Must operate on the raw string because <see cref="Uri"/> normalises dot segments automatically.
    /// </summary>
    public static bool HasDotSegments(string url)
    {
        // Extract the path portion (after scheme+authority, before query/fragment)
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            return false;

        var raw = uri.OriginalString;

        // Find the path start: skip past scheme://authority
        var authorityEnd = raw.IndexOf("//", StringComparison.Ordinal);
        if (authorityEnd < 0) return false;
        var pathStart = raw.IndexOf('/', authorityEnd + 2);
        if (pathStart < 0) return false;

        // Trim query and fragment
        var pathEnd = raw.IndexOfAny(['?', '#'], pathStart);
        var path = pathEnd < 0 ? raw[pathStart..] : raw[pathStart..pathEnd];

        var segments = path.Split('/');
        return segments.Any(s => s is "." or "..");
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

