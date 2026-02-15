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
/// Tests that AuthorizationService handles an optional 'me' parameter
/// per IndieAuth spec §5.2 (AUTH-7 / GAP-14). When me is absent the server
/// redirects to a profile URL entry form instead of returning an error.
/// </summary>
public class AuthorizationServiceOptionalMeTests : IDisposable
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

    public AuthorizationServiceOptionalMeTests()
    {
        var options = new DbContextOptionsBuilder<TalosDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        _dbContext = new TalosDbContext(options);

        _mockProfileDiscovery = new Mock<IProfileDiscoveryService>();
        _mockClientDiscovery = new Mock<IClientDiscoveryService>();
        _mockClientDiscovery.Setup(x => x.DiscoverClientAsync(It.IsAny<string>()))
            .ReturnsAsync((string cid) => new ClientInfo
            {
                ClientId = cid,
                ClientName = "Test App",
                LogoUri = "https://app.example.com/logo.png",
                WasFetched = true
            });
        _mockPkceService = new Mock<IPkceService>();
        _mockProviderFactory = new Mock<IIdentityProviderFactory>();
        _mockLogger = new Mock<ILogger<AuthorizationService>>();
    }

    public void Dispose()
    {
        _dbContext.Dispose();
    }

    private AuthorizationService CreateService(TalosSettings? talosOverride = null)
    {
        var talosOptions = new Mock<IOptions<TalosSettings>>();
        talosOptions.Setup(x => x.Value).Returns(talosOverride ?? _talosSettings);

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

    private static AuthorizationRequest CreateValidRequestWithoutMe()
    {
        return new AuthorizationRequest
        {
            ResponseType = "code",
            ClientId = "https://app.example.com/",
            RedirectUri = "https://app.example.com/callback",
            State = "test-state",
            CodeChallenge = "E9Melhoa2OwvFrEMTJguCHaoeK1t8URWbuGJSstw-cM",
            CodeChallengeMethod = "S256",
            Scope = "profile",
            Me = null
        };
    }

    private static AuthorizationRequest CreateValidRequestWithMe(string me = "https://user.example.com/")
    {
        var request = CreateValidRequestWithoutMe();
        request.Me = me;
        return request;
    }

    private void SetupSuccessfulProfileDiscovery()
    {
        _mockProfileDiscovery
            .Setup(x => x.DiscoverProfileAsync(It.IsAny<string>()))
            .ReturnsAsync(new ProfileDiscoveryResult
            {
                Success = true,
                ProfileUrl = "https://user.example.com/",
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

    // ===== No me → redirect to enter-profile =====

    [Fact]
    public async Task CreateAuthorizationAsync_NoMe_ReturnsEnterProfileRedirect()
    {
        var sut = CreateService();
        var request = CreateValidRequestWithoutMe();

        var result = await sut.CreateAuthorizationAsync(request);

        result.Success.Should().BeTrue();
        result.RedirectUrl.Should().StartWith("/enter-profile?");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public async Task CreateAuthorizationAsync_NullOrEmptyMe_ReturnsEnterProfileRedirect(string? me)
    {
        var sut = CreateService();
        var request = CreateValidRequestWithoutMe();
        request.Me = me;

        var result = await sut.CreateAuthorizationAsync(request);

        result.Success.Should().BeTrue();
        result.RedirectUrl.Should().StartWith("/enter-profile?");
    }

    // ===== Redirect contains original params =====

    [Fact]
    public async Task CreateAuthorizationAsync_NoMe_RedirectContainsOriginalParams()
    {
        var sut = CreateService();
        var request = CreateValidRequestWithoutMe();

        var result = await sut.CreateAuthorizationAsync(request);

        result.RedirectUrl.Should().Contain("client_id=");
        result.RedirectUrl.Should().Contain("redirect_uri=");
        result.RedirectUrl.Should().Contain("state=test-state");
        result.RedirectUrl.Should().Contain("code_challenge=");
        result.RedirectUrl.Should().Contain("code_challenge_method=S256");
        result.RedirectUrl.Should().Contain("response_type=code");
    }

    // ===== Redirect contains client display info =====

    [Fact]
    public async Task CreateAuthorizationAsync_NoMe_RedirectContainsClientInfo()
    {
        var sut = CreateService();
        var request = CreateValidRequestWithoutMe();

        var result = await sut.CreateAuthorizationAsync(request);

        result.RedirectUrl.Should().Contain("client_name=Test%20App");
        result.RedirectUrl.Should().Contain("client_logo=");
    }

    // ===== Client discovery is called =====

    [Fact]
    public async Task CreateAuthorizationAsync_NoMe_FetchesClientInfo()
    {
        var sut = CreateService();
        var request = CreateValidRequestWithoutMe();

        await sut.CreateAuthorizationAsync(request);

        _mockClientDiscovery.Verify(
            x => x.DiscoverClientAsync("https://app.example.com/"),
            Times.Once);
    }

    // ===== No pending session created =====

    [Fact]
    public async Task CreateAuthorizationAsync_NoMe_NoPendingSessionCreated()
    {
        var sut = CreateService();
        var request = CreateValidRequestWithoutMe();

        await sut.CreateAuthorizationAsync(request);

        _dbContext.PendingAuthentications.Should().BeEmpty(
            "no pending session should be created when me is absent — the form will redirect back to /auth");
    }

    // ===== Profile discovery is NOT called =====

    [Fact]
    public async Task CreateAuthorizationAsync_NoMe_DoesNotCallProfileDiscovery()
    {
        var sut = CreateService();
        var request = CreateValidRequestWithoutMe();

        await sut.CreateAuthorizationAsync(request);

        _mockProfileDiscovery.Verify(
            x => x.DiscoverProfileAsync(It.IsAny<string>()),
            Times.Never,
            "profile discovery should not happen when me is absent");
    }

    // ===== Invalid client_id still fails (me check comes after) =====

    [Fact]
    public async Task CreateAuthorizationAsync_NoMe_InvalidClientId_StillFailsValidation()
    {
        var sut = CreateService();
        var request = CreateValidRequestWithoutMe();
        request.ClientId = "not-a-url";

        var result = await sut.CreateAuthorizationAsync(request);

        result.Success.Should().BeFalse();
        result.Error.Should().Be("invalid_request");
    }

    // ===== With me still works (no regression) =====

    [Fact]
    public async Task CreateAuthorizationAsync_WithMe_StillWorks()
    {
        SetupSuccessfulProfileDiscovery();
        var sut = CreateService();
        var request = CreateValidRequestWithMe();

        var result = await sut.CreateAuthorizationAsync(request);

        result.Success.Should().BeTrue();
        result.RedirectUrl.Should().NotStartWith("/enter-profile",
            "when me IS provided, the normal flow should proceed");
    }

    // ===== Scope is preserved in redirect =====

    [Fact]
    public async Task CreateAuthorizationAsync_NoMe_RedirectPreservesScope()
    {
        var sut = CreateService();
        var request = CreateValidRequestWithoutMe();
        request.Scope = "profile email";

        var result = await sut.CreateAuthorizationAsync(request);

        result.RedirectUrl.Should().Contain("scope=profile%20email");
    }

    // ===== Null client name/logo handled gracefully =====

    [Fact]
    public async Task CreateAuthorizationAsync_NoMe_NullClientInfo_OmitsDisplayParams()
    {
        _mockClientDiscovery.Setup(x => x.DiscoverClientAsync(It.IsAny<string>()))
            .ReturnsAsync((string cid) => new ClientInfo
            {
                ClientId = cid,
                ClientName = null,
                LogoUri = null,
                WasFetched = false
            });

        var sut = CreateService();
        var request = CreateValidRequestWithoutMe();

        var result = await sut.CreateAuthorizationAsync(request);

        result.Success.Should().BeTrue();
        result.RedirectUrl.Should().NotContain("client_name=");
        result.RedirectUrl.Should().NotContain("client_logo=");
    }
}
