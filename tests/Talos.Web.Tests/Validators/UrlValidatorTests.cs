using FluentAssertions;
using Talos.Web.Services;

namespace Talos.Web.Tests.Validators;

/// <summary>
/// Tests for URL validation per IndieAuth spec §3.2
/// https://indieauth.spec.indieweb.org/#url
/// </summary>
public class UrlValidatorTests
{
    // ===== IsValidHttpsUrl Tests =====

    [Theory]
    [InlineData("https://example.com/")]
    [InlineData("https://example.com")]
    [InlineData("https://example.com/path")]
    [InlineData("https://example.com/path/to/page")]
    [InlineData("https://example.com:443/")]
    [InlineData("https://example.com:8443/")]
    public void IsValidHttpsUrl_ValidHttpsUrls_ReturnsTrue(string url)
    {
        UrlValidator.IsValidHttpsUrl(url).Should().BeTrue();
    }

    [Theory]
    [InlineData("http://example.com/")]           // HTTP not allowed
    [InlineData("ftp://example.com/")]            // Wrong scheme
    [InlineData("https://example.com/#fragment")] // Fragments not allowed
    [InlineData("https://user:pass@example.com/")] // Userinfo not allowed
    [InlineData("")]
    [InlineData(null)]
    [InlineData("not-a-url")]
    public void IsValidHttpsUrl_InvalidUrls_ReturnsFalse(string? url)
    {
        UrlValidator.IsValidHttpsUrl(url).Should().BeFalse();
    }

    [Theory]
    [InlineData("http://localhost/")]
    [InlineData("http://localhost:8080/")]
    [InlineData("http://127.0.0.1/")]
    public void IsValidHttpsUrl_Localhost_AllowsHttp(string url)
    {
        UrlValidator.IsValidHttpsUrl(url).Should().BeTrue();
    }

    // ===== IsValidClientId Tests =====

    [Theory]
    [InlineData("https://app.example.com/")]
    [InlineData("https://app.example.com/client")]
    [InlineData("https://app.example.com/path/to/app")]
    [InlineData("https://app.example.com:8443/")]       // Port is allowed per §3.3
    [InlineData("https://app.example.com/?key=value")]   // Query strings are allowed
    [InlineData("http://localhost/")]
    [InlineData("http://localhost:8080/")]
    [InlineData("http://127.0.0.1/")]                    // IPv4 loopback allowed
    public void IsValidClientId_ValidClientIds_ReturnsTrue(string url)
    {
        UrlValidator.IsValidClientId(url).Should().BeTrue();
    }

    [Theory]
    [InlineData("http://app.example.com/")]        // HTTP not allowed for non-loopback
    [InlineData("https://192.168.1.1/")]          // Non-loopback IPv4
    [InlineData("https://10.0.0.1/")]             // Private IPv4
    [InlineData("ftp://app.example.com/")]         // Wrong scheme
    [InlineData("")]                               // Empty string
    [InlineData(null)]                             // Null
    [InlineData("not-a-url")]                      // Not a URL
    public void IsValidClientId_InvalidClientIds_ReturnsFalse(string? url)
    {
        UrlValidator.IsValidClientId(url).Should().BeFalse();
    }

    [Theory]
    [InlineData("https://app.example.com/foo/../bar")]  // Double-dot segment
    [InlineData("https://app.example.com/./path")]      // Single-dot segment
    public void IsValidClientId_DotSegments_ReturnsFalse(string url)
    {
        UrlValidator.IsValidClientId(url).Should().BeFalse();
    }

    [Theory]
    [InlineData("https://app.example.com/#fragment")]    // Fragment (caught by IsValidHttpsUrl)
    [InlineData("https://user:pass@app.example.com/")]   // Userinfo (caught by IsValidHttpsUrl)
    public void IsValidClientId_FragmentOrUserinfo_ReturnsFalse(string url)
    {
        UrlValidator.IsValidClientId(url).Should().BeFalse();
    }

    // ===== IsValidProfileUrl Tests (§3.2) =====

    [Theory]
    [InlineData("https://example.com/")]              // Basic valid
    [InlineData("https://example.com/username")]       // Path without trailing slash
    [InlineData("https://example.com/users/1")]        // Deep path
    [InlineData("http://example.com/")]                // HTTP is allowed per §3.2
    [InlineData("https://example.com/?query=value")]   // Query strings not prohibited
    [InlineData("https://example.com:443/")]           // Default HTTPS port (IsDefaultPort = true)
    [InlineData("http://example.com:80/")]             // Default HTTP port (IsDefaultPort = true)
    public void IsValidProfileUrl_ValidUrls_ReturnsTrue(string url)
    {
        UrlValidator.IsValidProfileUrl(url).Should().BeTrue();
    }

    [Theory]
    [InlineData("ftp://example.com/")]                 // Wrong scheme (ID-2)
    [InlineData("mailto:user@example.com")]            // Wrong scheme (ID-2)
    [InlineData("https://example.com/foo/../bar")]     // Double-dot segment (ID-4)
    [InlineData("https://example.com/./path")]         // Single-dot segment (ID-4)
    [InlineData("https://example.com/#fragment")]      // Fragment (ID-5)
    [InlineData("https://example.com/path#frag")]      // Fragment on path (ID-5)
    [InlineData("https://user:pass@example.com/")]     // Userinfo (ID-6)
    [InlineData("https://example.com:8443/")]          // Non-default port (ID-7)
    [InlineData("http://example.com:9090/")]           // Non-default port HTTP (ID-7)
    [InlineData("https://192.168.1.1/")]               // IPv4 (ID-8)
    [InlineData("https://127.0.0.1/")]                 // Loopback IPv4 — no exception for profiles (ID-8)
    [InlineData("https://10.0.0.1/")]                  // Private IPv4 (ID-8)
    [InlineData("")]                                    // Empty
    [InlineData(null)]                                  // Null
    [InlineData("not-a-url")]                           // Not a URL
    public void IsValidProfileUrl_InvalidUrls_ReturnsFalse(string? url)
    {
        UrlValidator.IsValidProfileUrl(url).Should().BeFalse();
    }

    // ===== IsValidRedirectUri Tests =====

    [Theory]
    [InlineData("https://app.example.com/", "https://app.example.com/callback")]
    [InlineData("https://app.example.com/", "https://app.example.com/auth/callback")]
    [InlineData("https://app.example.com:443/", "https://app.example.com:443/callback")]
    public void IsValidRedirectUri_SameHost_ReturnsTrue(string clientId, string redirectUri)
    {
        UrlValidator.IsValidRedirectUri(redirectUri, clientId).Should().BeTrue();
    }

    [Theory]
    [InlineData("https://app.example.com/", "https://other.example.com/callback")]  // Different host
    [InlineData("https://app.example.com/", "http://app.example.com/callback")]     // Different scheme
    [InlineData("https://app.example.com:443/", "https://app.example.com:8443/cb")] // Different port
    public void IsValidRedirectUri_DifferentHost_ReturnsFalse(string clientId, string redirectUri)
    {
        UrlValidator.IsValidRedirectUri(redirectUri, clientId).Should().BeFalse();
    }

    // ===== NormalizeProfileUrl Tests =====

    [Theory]
    [InlineData("example.com", "https://example.com/")]
    [InlineData("https://example.com", "https://example.com/")]
    [InlineData("https://EXAMPLE.COM/", "https://example.com/")]
    [InlineData("https://example.com/PATH", "https://example.com/PATH")]
    public void NormalizeProfileUrl_NormalizesCorrectly(string input, string expected)
    {
        UrlValidator.NormalizeProfileUrl(input).Should().Be(expected);
    }
}

