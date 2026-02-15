using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Talos.Web.Configuration;
using Talos.Web.Data;
using Talos.Web.Models;
using Talos.Web.Services;
using Talos.Web.Services.IdentityProviders;

namespace Talos.Web.Tests.Services;

/// <summary>
/// Tests that AuthorizationService validates redirect_uri per IndieAuth spec §4.2.2, §5.2, §10.1.
/// Covers GAP-3: redirect_uri validation must be wired up in the authorization flow.
/// </summary>
public class AuthorizationServiceRedirectUriTests : IDisposable
{
    private readonly TalosDbContext _dbContext;
    private readonly Mock<IProfileDiscoveryService> _mockProfileDiscovery;
    private readonly Mock<IClientDiscoveryService> _mockClientDiscovery;
    private readonly Mock<IPkceService> _mockPkceService;
    private readonly Mock<IIdentityProviderFactory> _mockProviderFactory;
    private readonly Mock<ILogger<AuthorizationService>> _mockLogger;

    private readonly TalosSettings _talosSettings = new()
    {
        BaseUrl = "https://talos.example.com"
    };

    private readonly IndieAuthSettings _indieAuthSettings = new()
    {
        AuthorizationCodeExpirationMinutes = 10,
        RefreshTokenExpirationDays = 30,
        PendingAuthenticationExpirationMinutes = 30
    };

    public AuthorizationServiceRedirectUriTests()
    {
        var options = new DbContextOptionsBuilder<TalosDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        _dbContext = new TalosDbContext(options);

        _mockProfileDiscovery = new Mock<IProfileDiscoveryService>();
        _mockClientDiscovery = new Mock<IClientDiscoveryService>();
        _mockClientDiscovery.Setup(x => x.DiscoverClientAsync(It.IsAny<string>()))
            .ReturnsAsync((string cid) => new Models.ClientInfo { ClientId = cid, WasFetched = false });
        _mockPkceService = new Mock<IPkceService>();
        _mockProviderFactory = new Mock<IIdentityProviderFactory>();
        _mockLogger = new Mock<ILogger<AuthorizationService>>();
    }

    public void Dispose()
    {
        _dbContext.Dispose();
    }

    private AuthorizationService CreateService()
    {
        var talosOptions = new Mock<IOptions<TalosSettings>>();
        talosOptions.Setup(x => x.Value).Returns(_talosSettings);

        var indieAuthOptions = new Mock<IOptions<IndieAuthSettings>>();
        indieAuthOptions.Setup(x => x.Value).Returns(_indieAuthSettings);

        return new AuthorizationService(
            _dbContext,
            _mockProfileDiscovery.Object,
            _mockClientDiscovery.Object,
            _mockPkceService.Object,
            _mockProviderFactory.Object,
            indieAuthOptions.Object,
            talosOptions.Object,
            _mockLogger.Object);
    }

    private static AuthorizationRequest CreateRequest(string redirectUri, string clientId = "https://app.example.com/")
    {
        return new AuthorizationRequest
        {
            ResponseType = "code",
            ClientId = clientId,
            RedirectUri = redirectUri,
            State = "test-state",
            CodeChallenge = "E9Melhoa2OwvFrEMTJguCHaoeK1t8URWbuGJSstw-cM",
            CodeChallengeMethod = "S256",
            Me = "https://example.com/",
            Scope = "profile"
        };
    }

    private void SetupSuccessfulProfileDiscovery()
    {
        _mockProfileDiscovery
            .Setup(x => x.DiscoverProfileAsync(It.IsAny<string>()))
            .ReturnsAsync(new ProfileDiscoveryResult
            {
                Success = true,
                ProfileUrl = "https://example.com/",
                Providers = new List<DiscoveredProvider>
                {
                    new() { Type = "github", Name = "GitHub", ProfileUrl = "https://github.com/testuser" }
                }
            });

        var mockProvider = new Mock<IIdentityProvider>();
        mockProvider.Setup(x => x.ProviderType).Returns("github");
        mockProvider.Setup(x => x.GetAuthorizationUrl(It.IsAny<string>(), It.IsAny<string>()))
            .Returns("https://github.com/login/oauth/authorize?state=test");

        _mockProviderFactory
            .Setup(x => x.GetProviderByType("github"))
            .Returns(mockProvider.Object);
    }

    // ===== Dangerous redirect_uri =====

    [Fact]
    public async Task CreateAuthorizationAsync_JavascriptRedirectUri_ReturnsError()
    {
        var sut = CreateService();
        var request = CreateRequest("javascript:alert(1)");

        var result = await sut.CreateAuthorizationAsync(request);

        result.Success.Should().BeFalse();
        result.Error.Should().Be("invalid_request");
        result.RedirectUriUntrusted.Should().BeTrue(
            "errors must not be redirected to a dangerous redirect_uri");
    }

    [Fact]
    public async Task CreateAuthorizationAsync_DataSchemeRedirectUri_ReturnsError()
    {
        var sut = CreateService();
        var request = CreateRequest("data:text/html,<h1>hi</h1>");

        var result = await sut.CreateAuthorizationAsync(request);

        result.Success.Should().BeFalse();
        result.Error.Should().Be("invalid_request");
        result.RedirectUriUntrusted.Should().BeTrue();
    }

    // ===== Cross-origin redirect_uri =====

    [Fact]
    public async Task CreateAuthorizationAsync_CrossOriginRedirectUri_ReturnsError()
    {
        var sut = CreateService();
        var request = CreateRequest("https://evil.com/callback");

        var result = await sut.CreateAuthorizationAsync(request);

        result.Success.Should().BeFalse();
        result.Error.Should().Be("invalid_request");
        result.RedirectUriUntrusted.Should().BeTrue();
    }

    // ===== Valid same-origin redirect_uri =====

    [Fact]
    public async Task CreateAuthorizationAsync_ValidSameOriginRedirectUri_Succeeds()
    {
        SetupSuccessfulProfileDiscovery();
        var sut = CreateService();
        var request = CreateRequest("https://app.example.com/callback");

        var result = await sut.CreateAuthorizationAsync(request);

        // Should pass redirect_uri validation and proceed to profile discovery
        result.Error.Should().NotBe("invalid_request",
            "a same-origin redirect_uri should pass validation");
        result.RedirectUriUntrusted.Should().BeFalse();
    }

    // ===== RedirectUriUntrusted flag =====

    [Fact]
    public async Task CreateAuthorizationAsync_InvalidRedirectUri_SetsUntrustedFlag()
    {
        var sut = CreateService();
        var request = CreateRequest("https://different-host.com/callback");

        var result = await sut.CreateAuthorizationAsync(request);

        result.Success.Should().BeFalse();
        result.RedirectUriUntrusted.Should().BeTrue(
            "when redirect_uri validation fails, the flag must be set so the controller does not redirect to it");
    }

    [Fact]
    public async Task CreateAuthorizationAsync_MissingRedirectUri_SetsUntrustedFlag()
    {
        var sut = CreateService();
        var request = CreateRequest("");

        var result = await sut.CreateAuthorizationAsync(request);

        result.Success.Should().BeFalse();
        result.Error.Should().Be("invalid_request");
        result.RedirectUriUntrusted.Should().BeTrue(
            "an empty redirect_uri is untrusted");
    }

    // ===== Non-redirect errors should NOT set untrusted flag =====

    [Fact]
    public async Task CreateAuthorizationAsync_OtherValidationError_DoesNotSetUntrustedFlag()
    {
        var sut = CreateService();
        var request = CreateRequest("https://app.example.com/callback");
        request.ResponseType = "invalid"; // This will fail before redirect_uri check

        var result = await sut.CreateAuthorizationAsync(request);

        result.Success.Should().BeFalse();
        result.Error.Should().Be("unsupported_response_type");
        result.RedirectUriUntrusted.Should().BeFalse(
            "errors unrelated to redirect_uri should still allow redirecting errors to the client");
    }
}
