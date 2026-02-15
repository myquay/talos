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
/// Tests that AuthorizationService validates the 'me' parameter (profile URL)
/// per IndieAuth spec §3.2. Covers GAP-5: profile URL validation.
/// </summary>
public class AuthorizationServiceProfileUrlTests : IDisposable
{
    private readonly TalosDbContext _dbContext;
    private readonly Mock<IProfileDiscoveryService> _mockProfileDiscovery;
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

    public AuthorizationServiceProfileUrlTests()
    {
        var options = new DbContextOptionsBuilder<TalosDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        _dbContext = new TalosDbContext(options);

        _mockProfileDiscovery = new Mock<IProfileDiscoveryService>();
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
            _mockPkceService.Object,
            _mockProviderFactory.Object,
            indieAuthOptions.Object,
            talosOptions.Object,
            _mockLogger.Object);
    }

    private static AuthorizationRequest CreateRequestWithMe(string me)
    {
        return new AuthorizationRequest
        {
            ResponseType = "code",
            ClientId = "https://app.example.com/",
            RedirectUri = "https://app.example.com/callback",
            State = "test-state",
            CodeChallenge = "E9Melhoa2OwvFrEMTJguCHaoeK1t8URWbuGJSstw-cM",
            CodeChallengeMethod = "S256",
            Me = me,
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

    // ===== Invalid profile URL: port =====

    [Fact]
    public async Task CreateAuthorizationAsync_ProfileUrlWithPort_ReturnsError()
    {
        var sut = CreateService();
        var request = CreateRequestWithMe("https://example.com:8443/");

        var result = await sut.CreateAuthorizationAsync(request);

        result.Success.Should().BeFalse();
        result.Error.Should().Be("invalid_request");
        result.ErrorDescription.Should().Contain("me");
    }

    // ===== Invalid profile URL: IP address =====

    [Fact]
    public async Task CreateAuthorizationAsync_ProfileUrlIsIpAddress_ReturnsError()
    {
        var sut = CreateService();
        var request = CreateRequestWithMe("https://192.168.1.1/");

        var result = await sut.CreateAuthorizationAsync(request);

        result.Success.Should().BeFalse();
        result.Error.Should().Be("invalid_request");
        result.ErrorDescription.Should().Contain("me");
    }

    [Fact]
    public async Task CreateAuthorizationAsync_ProfileUrlIsLoopback_ReturnsError()
    {
        var sut = CreateService();
        var request = CreateRequestWithMe("https://127.0.0.1/");

        var result = await sut.CreateAuthorizationAsync(request);

        result.Success.Should().BeFalse();
        result.Error.Should().Be("invalid_request");
        result.ErrorDescription.Should().Contain("me");
    }

    // ===== Invalid profile URL: fragment =====

    [Fact]
    public async Task CreateAuthorizationAsync_ProfileUrlWithFragment_ReturnsError()
    {
        var sut = CreateService();
        var request = CreateRequestWithMe("https://example.com/#frag");

        var result = await sut.CreateAuthorizationAsync(request);

        result.Success.Should().BeFalse();
        result.Error.Should().Be("invalid_request");
        result.ErrorDescription.Should().Contain("me");
    }

    // ===== Invalid profile URL: dot-segments =====

    [Fact]
    public async Task CreateAuthorizationAsync_ProfileUrlWithDotSegments_ReturnsError()
    {
        var sut = CreateService();
        var request = CreateRequestWithMe("https://example.com/foo/../bar");

        var result = await sut.CreateAuthorizationAsync(request);

        result.Success.Should().BeFalse();
        result.Error.Should().Be("invalid_request");
        result.ErrorDescription.Should().Contain("me");
    }

    // ===== Valid profile URL: HTTPS =====

    [Fact]
    public async Task CreateAuthorizationAsync_ValidHttpsProfileUrl_ProceedsPastValidation()
    {
        SetupSuccessfulProfileDiscovery();
        var sut = CreateService();
        var request = CreateRequestWithMe("https://example.com/");

        var result = await sut.CreateAuthorizationAsync(request);

        // Should pass profile URL validation — either succeeds or fails on a later check
        if (!result.Success)
        {
            result.ErrorDescription.Should().NotContain("me is not a valid profile URL");
        }
    }

    // ===== Valid profile URL: HTTP =====

    [Fact]
    public async Task CreateAuthorizationAsync_ValidHttpProfileUrl_ProceedsPastValidation()
    {
        SetupSuccessfulProfileDiscovery();
        var sut = CreateService();
        var request = CreateRequestWithMe("http://example.com/");

        var result = await sut.CreateAuthorizationAsync(request);

        if (!result.Success)
        {
            result.ErrorDescription.Should().NotContain("me is not a valid profile URL");
        }
    }

    // ===== Invalid profile URL does not reach profile discovery =====

    [Fact]
    public async Task CreateAuthorizationAsync_InvalidProfileUrl_DoesNotCallProfileDiscovery()
    {
        var sut = CreateService();
        var request = CreateRequestWithMe("https://192.168.1.1/");

        await sut.CreateAuthorizationAsync(request);

        _mockProfileDiscovery.Verify(
            x => x.DiscoverProfileAsync(It.IsAny<string>()),
            Times.Never,
            "profile discovery should not be called when profile URL is invalid");
    }

    // ===== Invalid profile URL: redirect_uri remains trusted =====

    [Fact]
    public async Task CreateAuthorizationAsync_InvalidProfileUrl_RedirectUriStillTrusted()
    {
        var sut = CreateService();
        var request = CreateRequestWithMe("https://10.0.0.1/");

        var result = await sut.CreateAuthorizationAsync(request);

        result.Success.Should().BeFalse();
        result.RedirectUriUntrusted.Should().BeFalse(
            "client_id and redirect_uri were already validated, so errors can be redirected to the client");
    }
}
