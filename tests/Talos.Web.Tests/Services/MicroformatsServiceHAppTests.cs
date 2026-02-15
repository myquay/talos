using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Talos.Web.Services;

namespace Talos.Web.Tests.Services;

/// <summary>
/// Tests for h-app microformat extraction in MicroformatsService.
/// Covers GAP-4: IndieAuth §4.2.2 (HTML client metadata via h-app).
/// </summary>
public class MicroformatsServiceHAppTests
{
    private readonly MicroformatsService _sut;

    public MicroformatsServiceHAppTests()
    {
        var mockLogger = new Mock<ILogger<MicroformatsService>>();
        _sut = new MicroformatsService(mockLogger.Object);
    }

    [Fact]
    public void Parse_HtmlWithHApp_ExtractsName()
    {
        var html = """
        <html><body>
            <div class="h-app">
                <p class="p-name">My Awesome App</p>
            </div>
        </body></html>
        """;

        var result = _sut.Parse(html, new Uri("https://app.example.com/"));

        result.AppName.Should().Be("My Awesome App");
    }

    [Fact]
    public void Parse_HtmlWithHApp_ExtractsUrl()
    {
        var html = """
        <html><body>
            <div class="h-app">
                <a class="u-url" href="https://app.example.com/">App Home</a>
            </div>
        </body></html>
        """;

        var result = _sut.Parse(html, new Uri("https://app.example.com/"));

        result.AppUrl.Should().Be("https://app.example.com/");
    }

    [Fact]
    public void Parse_HtmlWithHApp_ExtractsLogo()
    {
        var html = """
        <html><body>
            <div class="h-app">
                <img class="u-logo" src="/logo.png" />
            </div>
        </body></html>
        """;

        var result = _sut.Parse(html, new Uri("https://app.example.com/"));

        result.AppLogoUrl.Should().Be("https://app.example.com/logo.png");
    }

    [Fact]
    public void Parse_HtmlWithHApp_AllProperties()
    {
        var html = """
        <html><body>
            <div class="h-app">
                <img class="u-logo" src="https://app.example.com/icon.svg" />
                <a class="u-url p-name" href="https://app.example.com/">Cool Client</a>
            </div>
        </body></html>
        """;

        var result = _sut.Parse(html, new Uri("https://app.example.com/"));

        result.AppName.Should().Be("Cool Client");
        result.AppUrl.Should().Be("https://app.example.com/");
        result.AppLogoUrl.Should().Be("https://app.example.com/icon.svg");
    }

    [Fact]
    public void Parse_HtmlWithoutHApp_ReturnsNullAppFields()
    {
        var html = """
        <html><body>
            <div class="h-card">
                <p class="p-name">Not an app</p>
            </div>
        </body></html>
        """;

        var result = _sut.Parse(html, new Uri("https://example.com/"));

        result.AppName.Should().BeNull();
        result.AppUrl.Should().BeNull();
        result.AppLogoUrl.Should().BeNull();
    }

    [Fact]
    public void Parse_HtmlWithMultipleHApps_UsesFirst()
    {
        var html = """
        <html><body>
            <div class="h-app">
                <p class="p-name">First App</p>
            </div>
            <div class="h-app">
                <p class="p-name">Second App</p>
            </div>
        </body></html>
        """;

        var result = _sut.Parse(html, new Uri("https://app.example.com/"));

        result.AppName.Should().Be("First App");
    }

    [Fact]
    public void Parse_HtmlWithRelativeLogoUrl_ResolvesAgainstBase()
    {
        var html = """
        <html><body>
            <div class="h-app">
                <p class="p-name">App</p>
                <img class="u-logo" src="/images/logo.png" />
            </div>
        </body></html>
        """;

        var result = _sut.Parse(html, new Uri("https://cdn.example.com/app/"));

        result.AppLogoUrl.Should().Be("https://cdn.example.com/images/logo.png");
    }

    [Fact]
    public void Parse_HtmlWithHApp_ExistingRelsStillParsed()
    {
        var html = """
        <html><head>
            <link rel="me" href="https://github.com/testuser" />
        </head><body>
            <div class="h-app">
                <p class="p-name">Test App</p>
            </div>
        </body></html>
        """;

        var result = _sut.Parse(html, new Uri("https://app.example.com/"));

        result.AppName.Should().Be("Test App");
        result.RelMeLinks.Should().Contain("https://github.com/testuser");
    }
}
