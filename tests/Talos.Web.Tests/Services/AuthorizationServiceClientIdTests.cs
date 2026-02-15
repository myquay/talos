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
/// Tests that AuthorizationService validates client_id per IndieAuth spec §3.3.
/// Covers GAP-2: client_id URL validation must be wired up in the authorization flow.
/// </summary>
public class AuthorizationServiceClientIdTests : IDisposable
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

    public AuthorizationServiceClientIdTests()
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

    private static AuthorizationRequest CreateRequest(string clientId)
    {
        return new AuthorizationRequest
        {
            ResponseType = "code",
            ClientId = clientId,
            RedirectUri = clientId.TrimEnd('/') + "/callback",
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

    // ===== Invalid client_id: IP addresses =====

    [Fact]
    public async Task CreateAuthorizationAsync_ClientIdIsPrivateIp_ReturnsError()
    {
        var sut = CreateService();
        var request = CreateRequest("https://192.168.1.1/");

        var result = await sut.CreateAuthorizationAsync(request);

        result.Success.Should().BeFalse();
        result.Error.Should().Be("invalid_request");
        result.ErrorDescription.Should().Contain("client_id");
        result.RedirectUriUntrusted.Should().BeTrue(
            "when client_id is invalid, redirect_uri cannot be trusted");
    }

    [Fact]
    public async Task CreateAuthorizationAsync_ClientIdIsInternalIp_ReturnsError()
    {
        var sut = CreateService();
        var request = CreateRequest("https://10.0.0.1/");

        var result = await sut.CreateAuthorizationAsync(request);

        result.Success.Should().BeFalse();
        result.Error.Should().Be("invalid_request");
        result.RedirectUriUntrusted.Should().BeTrue();
    }

    // ===== Invalid client_id: dot-segments =====

    [Fact]
    public async Task CreateAuthorizationAsync_ClientIdWithDotSegments_ReturnsError()
    {
        var sut = CreateService();
        // Use a manually constructed request since the redirect_uri helper won't work well here
        var request = new AuthorizationRequest
        {
            ResponseType = "code",
            ClientId = "https://app.example.com/foo/../bar",
            RedirectUri = "https://app.example.com/callback",
            State = "test-state",
            CodeChallenge = "E9Melhoa2OwvFrEMTJguCHaoeK1t8URWbuGJSstw-cM",
            CodeChallengeMethod = "S256",
            Me = "https://example.com/",
            Scope = "profile"
        };

        var result = await sut.CreateAuthorizationAsync(request);

        result.Success.Should().BeFalse();
        result.Error.Should().Be("invalid_request");
        result.ErrorDescription.Should().Contain("client_id");
        result.RedirectUriUntrusted.Should().BeTrue();
    }

    // ===== Invalid client_id: fragment =====

    [Fact]
    public async Task CreateAuthorizationAsync_ClientIdWithFragment_ReturnsError()
    {
        var sut = CreateService();
        var request = new AuthorizationRequest
        {
            ResponseType = "code",
            ClientId = "https://app.example.com/#frag",
            RedirectUri = "https://app.example.com/callback",
            State = "test-state",
            CodeChallenge = "E9Melhoa2OwvFrEMTJguCHaoeK1t8URWbuGJSstw-cM",
            CodeChallengeMethod = "S256",
            Me = "https://example.com/",
            Scope = "profile"
        };

        var result = await sut.CreateAuthorizationAsync(request);

        result.Success.Should().BeFalse();
        result.Error.Should().Be("invalid_request");
        result.RedirectUriUntrusted.Should().BeTrue();
    }

    // ===== Invalid client_id: HTTP on non-localhost =====

    [Fact]
    public async Task CreateAuthorizationAsync_ClientIdHttpNonLocalhost_ReturnsError()
    {
        var sut = CreateService();
        var request = new AuthorizationRequest
        {
            ResponseType = "code",
            ClientId = "http://app.example.com/",
            RedirectUri = "http://app.example.com/callback",
            State = "test-state",
            CodeChallenge = "E9Melhoa2OwvFrEMTJguCHaoeK1t8URWbuGJSstw-cM",
            CodeChallengeMethod = "S256",
            Me = "https://example.com/",
            Scope = "profile"
        };

        var result = await sut.CreateAuthorizationAsync(request);

        result.Success.Should().BeFalse();
        result.Error.Should().Be("invalid_request");
        result.RedirectUriUntrusted.Should().BeTrue();
    }

    // ===== Valid client_id: HTTPS =====

    [Fact]
    public async Task CreateAuthorizationAsync_ValidHttpsClientId_ProceedsPastClientIdCheck()
    {
        SetupSuccessfulProfileDiscovery();
        var sut = CreateService();
        var request = CreateRequest("https://app.example.com/");

        var result = await sut.CreateAuthorizationAsync(request);

        // Should pass client_id validation — either succeeds fully or fails on a later check
        result.Error.Should().NotBe("invalid_request",
            "a valid HTTPS client_id should pass validation");
        // Specifically, the error description should NOT mention client_id
        if (!result.Success)
        {
            result.ErrorDescription.Should().NotContain("client_id");
        }
    }

    // ===== Valid client_id: localhost =====

    [Fact]
    public async Task CreateAuthorizationAsync_ValidLocalhostClientId_ProceedsPastClientIdCheck()
    {
        SetupSuccessfulProfileDiscovery();
        var sut = CreateService();
        var request = CreateRequest("http://localhost:8080/");

        var result = await sut.CreateAuthorizationAsync(request);

        // Should pass client_id validation
        if (!result.Success)
        {
            result.ErrorDescription.Should().NotContain("client_id");
        }
    }

    // ===== Invalid client_id does not reach profile discovery =====

    [Fact]
    public async Task CreateAuthorizationAsync_InvalidClientId_DoesNotCallProfileDiscovery()
    {
        var sut = CreateService();
        var request = CreateRequest("https://10.0.0.1/");

        await sut.CreateAuthorizationAsync(request);

        _mockProfileDiscovery.Verify(
            x => x.DiscoverProfileAsync(It.IsAny<string>()),
            Times.Never,
            "profile discovery should not be called when client_id is invalid");
    }
}
