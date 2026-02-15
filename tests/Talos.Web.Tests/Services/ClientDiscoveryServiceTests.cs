using System.Net;
using System.Net.Http.Headers;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Moq.Protected;
using Talos.Web.Models;
using Talos.Web.Services;

namespace Talos.Web.Tests.Services;

/// <summary>
/// Tests for ClientDiscoveryService — client_id fetching and metadata parsing.
/// Covers GAP-4: DISC-5, DISC-6, DISC-8, DISC-9.
/// </summary>
public class ClientDiscoveryServiceTests
{
    private readonly Mock<IHttpClientFactory> _mockHttpClientFactory;
    private readonly Mock<IMicroformatsService> _mockMicroformatsService;
    private readonly Mock<ILogger<ClientDiscoveryService>> _mockLogger;

    public ClientDiscoveryServiceTests()
    {
        _mockHttpClientFactory = new Mock<IHttpClientFactory>();
        _mockMicroformatsService = new Mock<IMicroformatsService>();
        _mockLogger = new Mock<ILogger<ClientDiscoveryService>>();
    }

    private ClientDiscoveryService CreateService(HttpResponseMessage? response = null)
    {
        var mockHandler = new Mock<HttpMessageHandler>();
        if (response != null)
        {
            mockHandler.Protected()
                .Setup<Task<HttpResponseMessage>>(
                    "SendAsync",
                    ItExpr.IsAny<HttpRequestMessage>(),
                    ItExpr.IsAny<CancellationToken>())
                .ReturnsAsync(response);
        }

        var httpClient = new HttpClient(mockHandler.Object);
        _mockHttpClientFactory.Setup(f => f.CreateClient("ClientDiscovery")).Returns(httpClient);

        return new ClientDiscoveryService(
            _mockHttpClientFactory.Object,
            _mockMicroformatsService.Object,
            _mockLogger.Object);
    }

    private ClientDiscoveryService CreateServiceWithException(Exception exception)
    {
        var mockHandler = new Mock<HttpMessageHandler>();
        mockHandler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ThrowsAsync(exception);

        var httpClient = new HttpClient(mockHandler.Object);
        _mockHttpClientFactory.Setup(f => f.CreateClient("ClientDiscovery")).Returns(httpClient);

        return new ClientDiscoveryService(
            _mockHttpClientFactory.Object,
            _mockMicroformatsService.Object,
            _mockLogger.Object);
    }

    // ===== DISC-6: Loopback addresses must not be fetched =====

    [Theory]
    [InlineData("http://localhost:8080/")]
    [InlineData("http://localhost/")]
    [InlineData("http://127.0.0.1/")]
    [InlineData("http://[::1]/")]
    public async Task DiscoverClientAsync_LoopbackClientId_DoesNotFetch(string clientId)
    {
        var sut = CreateService();

        var result = await sut.DiscoverClientAsync(clientId);

        result.ClientId.Should().Be(clientId);
        result.WasFetched.Should().BeFalse();
        result.ClientName.Should().BeNull();
        // Verify no HTTP call was made
        _mockHttpClientFactory.Verify(f => f.CreateClient(It.IsAny<string>()), Times.Never);
    }

    // ===== JSON metadata: successful parsing =====

    [Fact]
    public async Task DiscoverClientAsync_JsonMetadata_ParsesCorrectly()
    {
        var clientId = "https://app.example.com/";
        var json = """
        {
            "client_id": "https://app.example.com/",
            "client_name": "Example App",
            "client_uri": "https://app.example.com/",
            "logo_uri": "https://app.example.com/logo.png",
            "redirect_uris": ["https://app.example.com/callback", "https://other.example.com/redirect"]
        }
        """;

        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json)
        };
        response.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");

        var sut = CreateService(response);

        var result = await sut.DiscoverClientAsync(clientId);

        result.ClientId.Should().Be(clientId);
        result.WasFetched.Should().BeTrue();
        result.ClientName.Should().Be("Example App");
        result.ClientUri.Should().Be("https://app.example.com/");
        result.LogoUri.Should().Be("https://app.example.com/logo.png");
        result.RedirectUris.Should().HaveCount(2);
        result.RedirectUris.Should().Contain("https://app.example.com/callback");
        result.RedirectUris.Should().Contain("https://other.example.com/redirect");
    }

    // ===== DISC-8: client_id mismatch =====

    [Fact]
    public async Task DiscoverClientAsync_JsonMetadata_ClientIdMismatch_ReturnsFallback()
    {
        var clientId = "https://app.example.com/";
        var json = """
        {
            "client_id": "https://different.example.com/",
            "client_name": "Evil App"
        }
        """;

        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json)
        };
        response.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");

        var sut = CreateService(response);

        var result = await sut.DiscoverClientAsync(clientId);

        result.WasFetched.Should().BeFalse();
        result.ClientName.Should().BeNull();
    }

    // ===== DISC-8: client_uri not prefix of client_id =====

    [Fact]
    public async Task DiscoverClientAsync_JsonMetadata_ClientUriNotPrefix_ReturnsFallback()
    {
        var clientId = "https://app.example.com/";
        var json = """
        {
            "client_id": "https://app.example.com/",
            "client_name": "Suspicious App",
            "client_uri": "https://evil.com/"
        }
        """;

        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json)
        };
        response.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");

        var sut = CreateService(response);

        var result = await sut.DiscoverClientAsync(clientId);

        result.WasFetched.Should().BeFalse();
        result.ClientName.Should().BeNull();
    }

    // ===== JSON: redirect_uris extracted =====

    [Fact]
    public async Task DiscoverClientAsync_JsonMetadata_RedirectUrisExtracted()
    {
        var clientId = "https://app.example.com/";
        var json = """
        {
            "client_id": "https://app.example.com/",
            "redirect_uris": ["https://other.example.com/cb"]
        }
        """;

        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json)
        };
        response.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");

        var sut = CreateService(response);

        var result = await sut.DiscoverClientAsync(clientId);

        result.WasFetched.Should().BeTrue();
        result.RedirectUris.Should().ContainSingle().Which.Should().Be("https://other.example.com/cb");
    }

    // ===== JSON: client_id in document is optional =====

    [Fact]
    public async Task DiscoverClientAsync_JsonMetadata_NoClientIdInDocument_Succeeds()
    {
        var clientId = "https://app.example.com/";
        var json = """
        {
            "client_name": "My App",
            "redirect_uris": ["https://app.example.com/cb"]
        }
        """;

        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json)
        };
        response.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");

        var sut = CreateService(response);

        var result = await sut.DiscoverClientAsync(clientId);

        result.WasFetched.Should().BeTrue();
        result.ClientName.Should().Be("My App");
    }

    // ===== HTML with h-app =====

    [Fact]
    public async Task DiscoverClientAsync_HtmlWithHApp_ParsesNameAndLogo()
    {
        var clientId = "https://app.example.com/";
        var html = "<html><body><div class=\"h-app\"><p class=\"p-name\">Cool App</p></div></body></html>";

        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(html)
        };
        response.Content.Headers.ContentType = new MediaTypeHeaderValue("text/html");

        _mockMicroformatsService.Setup(s => s.Parse(html, It.IsAny<Uri>()))
            .Returns(new MicroformatsResult
            {
                AppName = "Cool App",
                AppLogoUrl = "https://app.example.com/logo.png",
                AppUrl = "https://app.example.com/"
            });

        var sut = CreateService(response);

        var result = await sut.DiscoverClientAsync(clientId);

        result.WasFetched.Should().BeTrue();
        result.ClientName.Should().Be("Cool App");
        result.LogoUri.Should().Be("https://app.example.com/logo.png");
        result.ClientUri.Should().Be("https://app.example.com/");
    }

    // ===== HTML without h-app =====

    [Fact]
    public async Task DiscoverClientAsync_HtmlWithoutHApp_ReturnsFetchedButNoName()
    {
        var clientId = "https://app.example.com/";
        var html = "<html><body>No h-app here</body></html>";

        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(html)
        };
        response.Content.Headers.ContentType = new MediaTypeHeaderValue("text/html");

        _mockMicroformatsService.Setup(s => s.Parse(html, It.IsAny<Uri>()))
            .Returns(new MicroformatsResult());

        var sut = CreateService(response);

        var result = await sut.DiscoverClientAsync(clientId);

        result.WasFetched.Should().BeTrue();
        result.ClientName.Should().BeNull();
        result.LogoUri.Should().BeNull();
    }

    // ===== HTTP error =====

    [Fact]
    public async Task DiscoverClientAsync_HttpError_ReturnsFallback()
    {
        var clientId = "https://app.example.com/";
        var response = new HttpResponseMessage(HttpStatusCode.NotFound);

        var sut = CreateService(response);

        var result = await sut.DiscoverClientAsync(clientId);

        result.ClientId.Should().Be(clientId);
        result.WasFetched.Should().BeFalse();
        result.ClientName.Should().BeNull();
    }

    // ===== Network error =====

    [Fact]
    public async Task DiscoverClientAsync_NetworkError_ReturnsFallback()
    {
        var clientId = "https://app.example.com/";
        var sut = CreateServiceWithException(new HttpRequestException("Connection refused"));

        var result = await sut.DiscoverClientAsync(clientId);

        result.ClientId.Should().Be(clientId);
        result.WasFetched.Should().BeFalse();
    }

    // ===== Timeout =====

    [Fact]
    public async Task DiscoverClientAsync_Timeout_ReturnsFallback()
    {
        var clientId = "https://app.example.com/";
        var sut = CreateServiceWithException(new TaskCanceledException("Timeout"));

        var result = await sut.DiscoverClientAsync(clientId);

        result.ClientId.Should().Be(clientId);
        result.WasFetched.Should().BeFalse();
    }

    // ===== Unsupported content type =====

    [Fact]
    public async Task DiscoverClientAsync_UnsupportedContentType_ReturnsFallback()
    {
        var clientId = "https://app.example.com/";
        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("binary data")
        };
        response.Content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");

        var sut = CreateService(response);

        var result = await sut.DiscoverClientAsync(clientId);

        result.ClientId.Should().Be(clientId);
        result.WasFetched.Should().BeFalse();
    }

    // ===== IsLoopback: tested indirectly via DiscoverClientAsync_LoopbackClientId_DoesNotFetch =====

    // ===== JSON: invalid JSON body =====

    [Fact]
    public async Task DiscoverClientAsync_InvalidJson_ReturnsFallback()
    {
        var clientId = "https://app.example.com/";
        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("not json {{{")
        };
        response.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");

        var sut = CreateService(response);

        var result = await sut.DiscoverClientAsync(clientId);

        result.WasFetched.Should().BeFalse();
        result.ClientName.Should().BeNull();
    }
}
