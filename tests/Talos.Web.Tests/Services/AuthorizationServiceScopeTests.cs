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
/// Tests that AuthorizationService correctly parses scopes per IndieAuth spec.
/// Covers GAP-17: ParseScopes must NOT add a default "profile" scope when none is provided.
/// Empty scope = authentication-only flow (no access token).
/// </summary>
public class AuthorizationServiceScopeTests : IDisposable
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

    public AuthorizationServiceScopeTests()
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

    private static AuthorizationRequest CreateValidRequest(string? scope)
    {
        return new AuthorizationRequest
        {
            ResponseType = "code",
            ClientId = "https://app.example.com/",
            RedirectUri = "https://app.example.com/callback",
            State = "test-state",
            CodeChallenge = "E9Melhoa2OwvFrEMTJguCHaoeK1t8URWbuGJSstw-cM",
            CodeChallengeMethod = "S256",
            Me = "https://example.com/",
            Scope = scope
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

    /// <summary>
    /// Helper to read back the stored scopes from the pending authentication session.
    /// </summary>
    private async Task<List<string>> GetStoredScopesAsync(AuthorizationService sut, string sessionId)
    {
        var pending = await sut.GetPendingAuthenticationAsync(sessionId);
        pending.Should().NotBeNull();
        return pending!.Scopes;
    }

    // ===== No scope → empty list (authentication-only) =====

    [Fact]
    public async Task CreateAuthorizationAsync_NullScope_StoresEmptyScopes()
    {
        SetupSuccessfulProfileDiscovery();
        var sut = CreateService();
        var request = CreateValidRequest(scope: null);

        var result = await sut.CreateAuthorizationAsync(request);

        result.Success.Should().BeTrue();
        var scopes = await GetStoredScopesAsync(sut, result.SessionId!);
        scopes.Should().BeEmpty("null scope means authentication-only — no default scope should be added");
    }

    [Fact]
    public async Task CreateAuthorizationAsync_EmptyScope_StoresEmptyScopes()
    {
        SetupSuccessfulProfileDiscovery();
        var sut = CreateService();
        var request = CreateValidRequest(scope: "");

        var result = await sut.CreateAuthorizationAsync(request);

        result.Success.Should().BeTrue();
        var scopes = await GetStoredScopesAsync(sut, result.SessionId!);
        scopes.Should().BeEmpty("empty scope means authentication-only");
    }

    [Fact]
    public async Task CreateAuthorizationAsync_WhitespaceScope_StoresEmptyScopes()
    {
        SetupSuccessfulProfileDiscovery();
        var sut = CreateService();
        var request = CreateValidRequest(scope: "   ");

        var result = await sut.CreateAuthorizationAsync(request);

        result.Success.Should().BeTrue();
        var scopes = await GetStoredScopesAsync(sut, result.SessionId!);
        scopes.Should().BeEmpty("whitespace-only scope means authentication-only");
    }

    // ===== Explicit scopes → stored correctly =====

    [Fact]
    public async Task CreateAuthorizationAsync_SingleScope_StoresCorrectly()
    {
        SetupSuccessfulProfileDiscovery();
        var sut = CreateService();
        var request = CreateValidRequest(scope: "profile");

        var result = await sut.CreateAuthorizationAsync(request);

        result.Success.Should().BeTrue();
        var scopes = await GetStoredScopesAsync(sut, result.SessionId!);
        scopes.Should().BeEquivalentTo(new[] { "profile" });
    }

    [Fact]
    public async Task CreateAuthorizationAsync_MultipleScopes_StoresCorrectly()
    {
        SetupSuccessfulProfileDiscovery();
        var sut = CreateService();
        var request = CreateValidRequest(scope: "profile email create");

        var result = await sut.CreateAuthorizationAsync(request);

        result.Success.Should().BeTrue();
        var scopes = await GetStoredScopesAsync(sut, result.SessionId!);
        scopes.Should().BeEquivalentTo(new[] { "profile", "email", "create" });
    }

    [Fact]
    public async Task CreateAuthorizationAsync_ScopeWithExtraSpaces_NormalizesCorrectly()
    {
        SetupSuccessfulProfileDiscovery();
        var sut = CreateService();
        var request = CreateValidRequest(scope: "  profile   email  ");

        var result = await sut.CreateAuthorizationAsync(request);

        result.Success.Should().BeTrue();
        var scopes = await GetStoredScopesAsync(sut, result.SessionId!);
        scopes.Should().BeEquivalentTo(new[] { "profile", "email" });
    }
}
