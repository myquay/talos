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
    [InlineData("http://localhost/")]
    [InlineData("http://localhost:8080/")]
    public void IsValidClientId_ValidClientIds_ReturnsTrue(string url)
    {
        UrlValidator.IsValidClientId(url).Should().BeTrue();
    }

    [Theory]
    [InlineData("http://app.example.com/")]        // HTTP not allowed
    [InlineData("https://192.168.1.1/")]          // IP addresses not allowed (except localhost)
    [InlineData("https://10.0.0.1/")]
    public void IsValidClientId_InvalidClientIds_ReturnsFalse(string url)
    {
        UrlValidator.IsValidClientId(url).Should().BeFalse();
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

