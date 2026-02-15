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
/// Tests for cross-origin redirect_uri validation via client_id discovery.
/// Covers GAP-4: DISC-9 (cross-origin redirect_uri must be verified against published list).
/// </summary>
public class AuthorizationServiceClientDiscoveryTests : IDisposable
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

    public AuthorizationServiceClientDiscoveryTests()
    {
        var options = new DbContextOptionsBuilder<TalosDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        _dbContext = new TalosDbContext(options);

        _mockProfileDiscovery = new Mock<IProfileDiscoveryService>();
        _mockClientDiscovery = new Mock<IClientDiscoveryService>();
        _mockClientDiscovery.Setup(x => x.DiscoverClientAsync(It.IsAny<string>()))
            .ReturnsAsync((string cid) => new ClientInfo { ClientId = cid, WasFetched = false });
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

    private static AuthorizationRequest CreateRequest(
        string redirectUri,
        string clientId = "https://app.example.com/")
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

    // ===== Cross-origin redirect validated via published list (DISC-9) =====

    [Fact]
    public async Task CrossOriginRedirectUri_InPublishedList_Succeeds()
    {
        SetupSuccessfulProfileDiscovery();

        _mockClientDiscovery.Setup(x => x.DiscoverClientAsync("https://app.example.com/"))
            .ReturnsAsync(new ClientInfo
            {
                ClientId = "https://app.example.com/",
                ClientName = "Example App",
                RedirectUris = ["https://other.example.com/callback"],
                WasFetched = true
            });

        var sut = CreateService();
        var request = CreateRequest("https://other.example.com/callback");

        var result = await sut.CreateAuthorizationAsync(request);

        result.Success.Should().BeTrue("cross-origin redirect_uri in published list should be allowed");
        result.RedirectUriUntrusted.Should().BeFalse();
    }

    [Fact]
    public async Task CrossOriginRedirectUri_NotInPublishedList_Rejected()
    {
        _mockClientDiscovery.Setup(x => x.DiscoverClientAsync("https://app.example.com/"))
            .ReturnsAsync(new ClientInfo
            {
                ClientId = "https://app.example.com/",
                RedirectUris = ["https://allowed.example.com/callback"],
                WasFetched = true
            });

        var sut = CreateService();
        var request = CreateRequest("https://evil.example.com/callback");

        var result = await sut.CreateAuthorizationAsync(request);

        result.Success.Should().BeFalse();
        result.Error.Should().Be("invalid_request");
        result.RedirectUriUntrusted.Should().BeTrue();
    }

    [Fact]
    public async Task CrossOriginRedirectUri_ClientDiscoveryFailed_Rejected()
    {
        // Default mock returns WasFetched=false
        var sut = CreateService();
        var request = CreateRequest("https://other.example.com/callback");

        var result = await sut.CreateAuthorizationAsync(request);

        result.Success.Should().BeFalse();
        result.Error.Should().Be("invalid_request");
        result.RedirectUriUntrusted.Should().BeTrue();
    }

    [Fact]
    public async Task CrossOriginRedirectUri_EmptyPublishedList_Rejected()
    {
        _mockClientDiscovery.Setup(x => x.DiscoverClientAsync("https://app.example.com/"))
            .ReturnsAsync(new ClientInfo
            {
                ClientId = "https://app.example.com/",
                RedirectUris = [],
                WasFetched = true
            });

        var sut = CreateService();
        var request = CreateRequest("https://other.example.com/callback");

        var result = await sut.CreateAuthorizationAsync(request);

        result.Success.Should().BeFalse();
        result.RedirectUriUntrusted.Should().BeTrue();
    }

    // ===== Client info stored in pending session =====

    [Fact]
    public async Task SameOriginRedirect_ClientInfoStoredInPending()
    {
        SetupSuccessfulProfileDiscovery();

        _mockClientDiscovery.Setup(x => x.DiscoverClientAsync("https://app.example.com/"))
            .ReturnsAsync(new ClientInfo
            {
                ClientId = "https://app.example.com/",
                ClientName = "My Cool App",
                LogoUri = "https://app.example.com/logo.png",
                WasFetched = true
            });

        var sut = CreateService();
        var request = CreateRequest("https://app.example.com/callback");

        var result = await sut.CreateAuthorizationAsync(request);

        result.Success.Should().BeTrue();

        // Verify client info was stored in the pending entity
        var pending = await _dbContext.PendingAuthentications
            .FirstOrDefaultAsync(p => p.SessionId == result.SessionId);

        pending.Should().NotBeNull();
        pending!.ClientName.Should().Be("My Cool App");
        pending.ClientLogoUri.Should().Be("https://app.example.com/logo.png");
    }

    [Fact]
    public async Task SameOriginRedirect_ClientDiscoveryFailed_ProceedsWithNulls()
    {
        SetupSuccessfulProfileDiscovery();

        // Default mock returns WasFetched=false with null name/logo
        var sut = CreateService();
        var request = CreateRequest("https://app.example.com/callback");

        var result = await sut.CreateAuthorizationAsync(request);

        // Should still succeed — client discovery failure is non-fatal for same-origin
        result.Success.Should().BeTrue();
    }

    // ===== Client discovery is called =====

    [Fact]
    public async Task SameOriginRedirect_FetchesClientInfo()
    {
        SetupSuccessfulProfileDiscovery();
        _mockClientDiscovery.Setup(x => x.DiscoverClientAsync("https://app.example.com/"))
            .ReturnsAsync(new ClientInfo
            {
                ClientId = "https://app.example.com/",
                WasFetched = true
            });

        var sut = CreateService();
        var request = CreateRequest("https://app.example.com/callback");

        await sut.CreateAuthorizationAsync(request);

        _mockClientDiscovery.Verify(
            x => x.DiscoverClientAsync("https://app.example.com/"),
            Times.Once,
            "client discovery should be called for display purposes");
    }

    // ===== Cross-origin redirect triggers early client discovery =====

    [Fact]
    public async Task CrossOriginRedirect_ClientDiscoveredDuringRedirectValidation()
    {
        SetupSuccessfulProfileDiscovery();

        _mockClientDiscovery.Setup(x => x.DiscoverClientAsync("https://app.example.com/"))
            .ReturnsAsync(new ClientInfo
            {
                ClientId = "https://app.example.com/",
                ClientName = "Cross-Origin App",
                RedirectUris = ["https://other.example.com/callback"],
                WasFetched = true
            });

        var sut = CreateService();
        var request = CreateRequest("https://other.example.com/callback");

        var result = await sut.CreateAuthorizationAsync(request);

        result.Success.Should().BeTrue();

        // Client discovery should be called exactly once, not twice
        _mockClientDiscovery.Verify(
            x => x.DiscoverClientAsync("https://app.example.com/"),
            Times.Once);
    }

    // ===== GetPendingAuthenticationAsync maps client fields =====

    [Fact]
    public async Task GetPendingAuthenticationAsync_MapsClientNameAndLogo()
    {
        SetupSuccessfulProfileDiscovery();

        _mockClientDiscovery.Setup(x => x.DiscoverClientAsync("https://app.example.com/"))
            .ReturnsAsync(new ClientInfo
            {
                ClientId = "https://app.example.com/",
                ClientName = "Test App",
                LogoUri = "https://app.example.com/icon.svg",
                WasFetched = true
            });

        var sut = CreateService();
        var request = CreateRequest("https://app.example.com/callback");
        var authResult = await sut.CreateAuthorizationAsync(request);

        var pending = await sut.GetPendingAuthenticationAsync(authResult.SessionId!);

        pending.Should().NotBeNull();
        pending!.ClientName.Should().Be("Test App");
        pending.ClientLogoUri.Should().Be("https://app.example.com/icon.svg");
    }
}
