using FluentAssertions;
using Talos.Web.Services;

namespace Talos.Web.Tests.Validators;

/// <summary>
/// Tests for redirect_uri validation per IndieAuth spec §4.2.2, §5.2, §10.1.
/// Covers GAP-3: redirect_uri must be validated before use.
/// </summary>
public class UrlValidatorRedirectUriTests
{
    // ===== Same-origin valid cases =====

    [Theory]
    [InlineData("https://app.example.com/", "https://app.example.com/callback")]
    [InlineData("https://app.example.com/", "https://app.example.com/auth/callback")]
    [InlineData("https://app.example.com/", "https://app.example.com/")]
    [InlineData("https://app.example.com:443/", "https://app.example.com:443/callback")]
    public void IsValidRedirectUri_SameOrigin_ReturnsTrue(string clientId, string redirectUri)
    {
        UrlValidator.IsValidRedirectUri(redirectUri, clientId).Should().BeTrue();
    }

    // ===== Null/empty =====

    [Fact]
    public void IsValidRedirectUri_NullRedirectUri_ReturnsFalse()
    {
        UrlValidator.IsValidRedirectUri(null, "https://app.example.com/").Should().BeFalse();
    }

    [Fact]
    public void IsValidRedirectUri_EmptyRedirectUri_ReturnsFalse()
    {
        UrlValidator.IsValidRedirectUri("", "https://app.example.com/").Should().BeFalse();
    }

    [Fact]
    public void IsValidRedirectUri_RelativePath_ReturnsFalse()
    {
        UrlValidator.IsValidRedirectUri("/callback", "https://app.example.com/").Should().BeFalse();
    }

    // ===== Dangerous schemes =====

    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData("JavaScript:alert(document.cookie)")]
    [InlineData("data:text/html,<h1>hi</h1>")]
    [InlineData("DATA:text/html;base64,abc")]
    [InlineData("vbscript:MsgBox(1)")]
    [InlineData("file:///etc/passwd")]
    public void IsValidRedirectUri_DangerousScheme_ReturnsFalse(string redirectUri)
    {
        UrlValidator.IsValidRedirectUri(redirectUri, "https://app.example.com/").Should().BeFalse();
    }

    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData("data:text/html,<h1>test</h1>")]
    [InlineData("vbscript:MsgBox(1)")]
    [InlineData("file:///etc/passwd")]
    public void HasDangerousScheme_DangerousSchemes_ReturnsTrue(string url)
    {
        UrlValidator.HasDangerousScheme(url).Should().BeTrue();
    }

    [Theory]
    [InlineData("https://example.com/")]
    [InlineData("http://localhost/")]
    [InlineData("")]
    [InlineData(null)]
    public void HasDangerousScheme_SafeOrEmpty_ReturnsFalse(string? url)
    {
        UrlValidator.HasDangerousScheme(url!).Should().BeFalse();
    }

    // ===== Non-HTTPS scheme =====

    [Fact]
    public void IsValidRedirectUri_FtpScheme_ReturnsFalse()
    {
        UrlValidator.IsValidRedirectUri("ftp://app.example.com/callback", "https://app.example.com/")
            .Should().BeFalse();
    }

    [Fact]
    public void IsValidRedirectUri_HttpSchemeNonLocalhost_ReturnsFalse()
    {
        // http redirect_uri against https client_id → scheme mismatch
        UrlValidator.IsValidRedirectUri("http://app.example.com/callback", "https://app.example.com/")
            .Should().BeFalse();
    }

    // ===== Cross-origin (rejected until client metadata verification is implemented) =====

    [Fact]
    public void IsValidRedirectUri_DifferentHost_ReturnsFalse()
    {
        UrlValidator.IsValidRedirectUri("https://evil.com/callback", "https://app.example.com/")
            .Should().BeFalse();
    }

    [Fact]
    public void IsValidRedirectUri_DifferentPort_ReturnsFalse()
    {
        UrlValidator.IsValidRedirectUri("https://app.example.com:8443/callback", "https://app.example.com/")
            .Should().BeFalse();
    }

    [Fact]
    public void IsValidRedirectUri_DifferentScheme_ReturnsFalse()
    {
        UrlValidator.IsValidRedirectUri("http://app.example.com/callback", "https://app.example.com/")
            .Should().BeFalse();
    }

    // ===== Fragment / userinfo =====

    [Fact]
    public void IsValidRedirectUri_WithFragment_ReturnsFalse()
    {
        UrlValidator.IsValidRedirectUri("https://app.example.com/callback#frag", "https://app.example.com/")
            .Should().BeFalse();
    }

    [Fact]
    public void IsValidRedirectUri_WithUserInfo_ReturnsFalse()
    {
        UrlValidator.IsValidRedirectUri("https://user:pass@app.example.com/callback", "https://app.example.com/")
            .Should().BeFalse();
    }

    // ===== Dot-segment path traversal =====

    [Theory]
    [InlineData("https://app.example.com/./callback")]
    [InlineData("https://app.example.com/../callback")]
    [InlineData("https://app.example.com/a/../b")]
    [InlineData("https://app.example.com/a/./b")]
    public void IsValidRedirectUri_DotSegments_ReturnsFalse(string redirectUri)
    {
        // Note: .NET's Uri normalizes dot-segments during parsing, so we also test
        // the HasDotSegments helper directly below for completeness.
        UrlValidator.IsValidRedirectUri(redirectUri, "https://app.example.com/")
            .Should().BeFalse();
    }

    [Fact]
    public void HasDotSegments_PathWithSingleDot_ReturnsTrue()
    {
        UrlValidator.HasDotSegments("https://example.com/./b").Should().BeTrue();
    }

    [Fact]
    public void HasDotSegments_PathWithDoubleDot_ReturnsTrue()
    {
        UrlValidator.HasDotSegments("https://example.com/a/../b").Should().BeTrue();
    }

    [Fact]
    public void HasDotSegments_CleanPath_ReturnsFalse()
    {
        UrlValidator.HasDotSegments("https://example.com/auth/callback").Should().BeFalse();
    }

    // ===== Localhost =====

    [Fact]
    public void IsValidRedirectUri_LocalhostSameOrigin_ReturnsTrue()
    {
        UrlValidator.IsValidRedirectUri("http://localhost:8080/callback", "http://localhost:8080/")
            .Should().BeTrue();
    }

    [Fact]
    public void IsValidRedirectUri_127001SameOrigin_ReturnsTrue()
    {
        UrlValidator.IsValidRedirectUri("http://127.0.0.1/callback", "http://127.0.0.1/")
            .Should().BeTrue();
    }

    [Fact]
    public void IsValidRedirectUri_LocalhostDifferentPort_ReturnsFalse()
    {
        UrlValidator.IsValidRedirectUri("http://localhost:9090/callback", "http://localhost:8080/")
            .Should().BeFalse();
    }
}
