using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Talos.Web.Services;

namespace Talos.Web.Tests.Services;

/// <summary>
/// Tests for MicroformatsService using the Microformats library.
/// Verifies rel="me" link discovery and IndieAuth endpoint extraction.
/// </summary>
public class MicroformatsServiceTests
{
    private readonly MicroformatsService _sut;

    public MicroformatsServiceTests()
    {
        var logger = Mock.Of<ILogger<MicroformatsService>>();
        _sut = new MicroformatsService(logger);
    }

    #region rel="me" Link Discovery

    [Fact]
    public void Parse_LinkTagWithRelMe_ExtractsHref()
    {
        var html = """
            <html>
            <head>
                <link rel="me" href="https://github.com/janedoe">
            </head>
            <body></body>
            </html>
            """;
        var baseUrl = new Uri("https://example.com/");

        var result = _sut.Parse(html, baseUrl);

        result.RelMeLinks.Should().Contain("https://github.com/janedoe");
    }

    [Fact]
    public void Parse_AnchorTagWithRelMe_ExtractsHref()
    {
        var html = """
            <html>
            <body>
                <a href="https://github.com/janedoe" rel="me">GitHub</a>
            </body>
            </html>
            """;
        var baseUrl = new Uri("https://example.com/");

        var result = _sut.Parse(html, baseUrl);

        result.RelMeLinks.Should().Contain("https://github.com/janedoe");
    }

    [Fact]
    public void Parse_MultipleRelMeLinks_ExtractsAll()
    {
        var html = """
            <html>
            <head>
                <link rel="me" href="https://github.com/janedoe">
            </head>
            <body>
                <a href="https://twitter.com/janedoe" rel="me">Twitter</a>
                <a href="https://mastodon.social/@janedoe" rel="me">Mastodon</a>
            </body>
            </html>
            """;
        var baseUrl = new Uri("https://example.com/");

        var result = _sut.Parse(html, baseUrl);

        result.RelMeLinks.Should().HaveCount(3);
        result.RelMeLinks.Should().Contain("https://github.com/janedoe");
        result.RelMeLinks.Should().Contain("https://twitter.com/janedoe");
        result.RelMeLinks.Should().Contain("https://mastodon.social/@janedoe");
    }

    [Fact]
    public void Parse_RelMeWithMultipleValues_ExtractsLink()
    {
        // The Microformats spec allows space-separated rel values
        var html = """
            <html>
            <body>
                <a href="https://github.com/janedoe" rel="me noopener">GitHub</a>
            </body>
            </html>
            """;
        var baseUrl = new Uri("https://example.com/");

        var result = _sut.Parse(html, baseUrl);

        result.RelMeLinks.Should().Contain("https://github.com/janedoe");
    }

    [Fact]
    public void Parse_NoRelMeLinks_ReturnsEmptyList()
    {
        var html = """
            <html>
            <body>
                <a href="https://github.com/janedoe">GitHub (no rel)</a>
            </body>
            </html>
            """;
        var baseUrl = new Uri("https://example.com/");

        var result = _sut.Parse(html, baseUrl);

        result.RelMeLinks.Should().BeEmpty();
    }

    [Fact]
    public void Parse_DuplicateLinks_Deduplicated()
    {
        var html = """
            <html>
            <head>
                <link rel="me" href="https://github.com/janedoe">
            </head>
            <body>
                <a href="https://github.com/janedoe" rel="me">GitHub</a>
            </body>
            </html>
            """;
        var baseUrl = new Uri("https://example.com/");

        var result = _sut.Parse(html, baseUrl);

        result.RelMeLinks.Should().HaveCount(1);
    }

    #endregion

    #region Relative URL Resolution

    [Fact]
    public void Parse_RelativeHref_ResolvesAgainstBase()
    {
        var html = """
            <html>
            <body>
                <a href="/about" rel="me">About</a>
            </body>
            </html>
            """;
        var baseUrl = new Uri("https://example.com/");

        var result = _sut.Parse(html, baseUrl);

        result.RelMeLinks.Should().Contain("https://example.com/about");
    }

    [Fact]
    public void Parse_RelativePathUrl_ResolvesCorrectly()
    {
        var html = """
            <html>
            <body>
                <a href="../profile" rel="me">Profile</a>
            </body>
            </html>
            """;
        var baseUrl = new Uri("https://example.com/page/subpage/");

        var result = _sut.Parse(html, baseUrl);

        result.RelMeLinks.Should().Contain("https://example.com/page/profile");
    }

    #endregion

    #region IndieAuth Endpoint Discovery

    [Fact]
    public void Parse_AuthorizationEndpoint_ExtractsUrl()
    {
        var html = """
            <html>
            <head>
                <link rel="authorization_endpoint" href="https://auth.example.com/authorize">
            </head>
            </html>
            """;
        var baseUrl = new Uri("https://example.com/");

        var result = _sut.Parse(html, baseUrl);

        result.AuthorizationEndpoint.Should().Be("https://auth.example.com/authorize");
    }

    [Fact]
    public void Parse_TokenEndpoint_ExtractsUrl()
    {
        var html = """
            <html>
            <head>
                <link rel="token_endpoint" href="https://auth.example.com/token">
            </head>
            </html>
            """;
        var baseUrl = new Uri("https://example.com/");

        var result = _sut.Parse(html, baseUrl);

        result.TokenEndpoint.Should().Be("https://auth.example.com/token");
    }

    [Fact]
    public void Parse_RelativeAuthorizationEndpoint_ResolvesUrl()
    {
        var html = """
            <html>
            <head>
                <link rel="authorization_endpoint" href="/auth">
            </head>
            </html>
            """;
        var baseUrl = new Uri("https://example.com/");

        var result = _sut.Parse(html, baseUrl);

        result.AuthorizationEndpoint.Should().Be("https://example.com/auth");
    }

    [Fact]
    public void Parse_MultipleEndpoints_ReturnsFirst()
    {
        // Per IndieAuth spec, use first discovered endpoint
        var html = """
            <html>
            <head>
                <link rel="authorization_endpoint" href="https://auth1.example.com">
                <link rel="authorization_endpoint" href="https://auth2.example.com">
            </head>
            </html>
            """;
        var baseUrl = new Uri("https://example.com/");

        var result = _sut.Parse(html, baseUrl);

        result.AuthorizationEndpoint.Should().Be("https://auth1.example.com/");
    }

    [Fact]
    public void Parse_AllIndieWebEndpoints_ExtractsAll()
    {
        var html = """
            <html>
            <head>
                <link rel="authorization_endpoint" href="https://example.com/auth">
                <link rel="token_endpoint" href="https://example.com/token">
                <link rel="indieauth-metadata" href="https://example.com/.well-known/oauth-authorization-server">
                <link rel="micropub" href="https://example.com/micropub">
                <link rel="microsub" href="https://example.com/microsub">
            </head>
            </html>
            """;
        var baseUrl = new Uri("https://example.com/");

        var result = _sut.Parse(html, baseUrl);

        result.AuthorizationEndpoint.Should().Be("https://example.com/auth");
        result.TokenEndpoint.Should().Be("https://example.com/token");
        result.IndieAuthMetadata.Should().Be("https://example.com/.well-known/oauth-authorization-server");
        result.Micropub.Should().Be("https://example.com/micropub");
        result.Microsub.Should().Be("https://example.com/microsub");
    }

    #endregion

    #region Edge Cases

    [Fact]
    public void Parse_EmptyHtml_ReturnsEmptyResult()
    {
        var html = "";
        var baseUrl = new Uri("https://example.com/");

        var result = _sut.Parse(html, baseUrl);

        result.RelMeLinks.Should().BeEmpty();
        result.AuthorizationEndpoint.Should().BeNull();
        result.TokenEndpoint.Should().BeNull();
    }

    [Fact]
    public void Parse_MalformedHtml_StillExtractsLinks()
    {
        var html = """
            <html>
            <body>
                <a href="https://github.com/janedoe" rel="me">GitHub
                <!-- Missing closing tag -->
            </body>
            """;
        var baseUrl = new Uri("https://example.com/");

        var result = _sut.Parse(html, baseUrl);

        result.RelMeLinks.Should().Contain("https://github.com/janedoe");
    }

    [Fact]
    public void Parse_EmptyHref_SkipsLink()
    {
        var html = """
            <html>
            <body>
                <a href="" rel="me">Empty</a>
                <a href="https://github.com/janedoe" rel="me">GitHub</a>
            </body>
            </html>
            """;
        var baseUrl = new Uri("https://example.com/");

        var result = _sut.Parse(html, baseUrl);

        result.RelMeLinks.Should().HaveCount(1);
        result.RelMeLinks.Should().NotContain("");
    }

    #endregion
}

