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
/// Tests for AuthorizationService - AllowedProfileHosts feature
/// </summary>
public class AuthorizationServiceAllowedHostsTests : IDisposable
{
    private readonly TalosDbContext _dbContext;
    private readonly Mock<IProfileDiscoveryService> _mockProfileDiscovery;
    private readonly Mock<IPkceService> _mockPkceService;
    private readonly Mock<IIdentityProviderFactory> _mockProviderFactory;
    private readonly IndieAuthSettings _indieAuthSettings;
    private readonly TalosSettings _talosSettings;
    private readonly Mock<ILogger<AuthorizationService>> _mockLogger;

    public AuthorizationServiceAllowedHostsTests()
    {
        // Setup in-memory database
        var options = new DbContextOptionsBuilder<TalosDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        _dbContext = new TalosDbContext(options);

        _mockProfileDiscovery = new Mock<IProfileDiscoveryService>();
        _mockPkceService = new Mock<IPkceService>();
        _mockProviderFactory = new Mock<IIdentityProviderFactory>();
        _mockLogger = new Mock<ILogger<AuthorizationService>>();

        _indieAuthSettings = new IndieAuthSettings
        {
            AuthorizationCodeExpirationMinutes = 10,
            RefreshTokenExpirationDays = 30,
            PendingAuthenticationExpirationMinutes = 30
        };

        _talosSettings = new TalosSettings
        {
            BaseUrl = "https://talos.example.com"
        };
    }

    public void Dispose()
    {
        _dbContext.Dispose();
    }

    private AuthorizationService CreateService(TalosSettings? settings = null)
    {
        var talosOptions = new Mock<IOptions<TalosSettings>>();
        talosOptions.Setup(x => x.Value).Returns(settings ?? _talosSettings);

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

    private static AuthorizationRequest CreateValidRequest(string me = "https://example.com/")
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

    // ===== AllowedProfileHosts Tests =====

    [Fact]
    public async Task CreateAuthorizationAsync_NullAllowedProfileHosts_AllowsAllHosts()
    {
        // Arrange
        var settings = new TalosSettings
        {
            BaseUrl = "https://talos.example.com",
            AllowedProfileHosts = null
        };
        var sut = CreateService(settings);
        SetupSuccessfulProfileDiscovery();
        var request = CreateValidRequest("https://anyhost.com/user");

        // Act
        var result = await sut.CreateAuthorizationAsync(request);

        // Assert
        result.Error.Should().NotBe("access_denied");
        // Should have proceeded to profile discovery
        _mockProfileDiscovery.Verify(x => x.DiscoverProfileAsync(It.IsAny<string>()), Times.Once);
    }

    [Fact]
    public async Task CreateAuthorizationAsync_EmptyAllowedProfileHosts_AllowsAllHosts()
    {
        // Arrange
        var settings = new TalosSettings
        {
            BaseUrl = "https://talos.example.com",
            AllowedProfileHosts = Array.Empty<string>()
        };
        var sut = CreateService(settings);
        SetupSuccessfulProfileDiscovery();
        var request = CreateValidRequest("https://anyhost.com/user");

        // Act
        var result = await sut.CreateAuthorizationAsync(request);

        // Assert
        result.Error.Should().NotBe("access_denied");
        // Should have proceeded to profile discovery
        _mockProfileDiscovery.Verify(x => x.DiscoverProfileAsync(It.IsAny<string>()), Times.Once);
    }

    [Fact]
    public async Task CreateAuthorizationAsync_MatchingHost_Allowed()
    {
        // Arrange
        var settings = new TalosSettings
        {
            BaseUrl = "https://talos.example.com",
            AllowedProfileHosts = new[] { "example.com" }
        };
        var sut = CreateService(settings);
        SetupSuccessfulProfileDiscovery();
        var request = CreateValidRequest("https://example.com/");

        // Act
        var result = await sut.CreateAuthorizationAsync(request);

        // Assert
        result.Error.Should().NotBe("access_denied");
        // Should have proceeded to profile discovery
        _mockProfileDiscovery.Verify(x => x.DiscoverProfileAsync(It.IsAny<string>()), Times.Once);
    }

    [Fact]
    public async Task CreateAuthorizationAsync_NonMatchingHost_Denied()
    {
        // Arrange
        var settings = new TalosSettings
        {
            BaseUrl = "https://talos.example.com",
            AllowedProfileHosts = new[] { "example.com" }
        };
        var sut = CreateService(settings);
        var request = CreateValidRequest("https://other.com/");

        // Act
        var result = await sut.CreateAuthorizationAsync(request);

        // Assert
        result.Success.Should().BeFalse();
        result.Error.Should().Be("access_denied");
        result.ErrorDescription.Should().Be("This server is not configured to authenticate users from the requested website.");
        // Should NOT have proceeded to profile discovery
        _mockProfileDiscovery.Verify(x => x.DiscoverProfileAsync(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task CreateAuthorizationAsync_CaseInsensitiveMatching_Allowed()
    {
        // Arrange
        var settings = new TalosSettings
        {
            BaseUrl = "https://talos.example.com",
            AllowedProfileHosts = new[] { "Example.COM" }
        };
        var sut = CreateService(settings);
        SetupSuccessfulProfileDiscovery();
        var request = CreateValidRequest("https://example.com/user");

        // Act
        var result = await sut.CreateAuthorizationAsync(request);

        // Assert
        result.Error.Should().NotBe("access_denied");
        // Should have proceeded to profile discovery
        _mockProfileDiscovery.Verify(x => x.DiscoverProfileAsync(It.IsAny<string>()), Times.Once);
    }

    [Fact]
    public async Task CreateAuthorizationAsync_SubdomainNotMatchedByParentDomain_Denied()
    {
        // Arrange
        var settings = new TalosSettings
        {
            BaseUrl = "https://talos.example.com",
            AllowedProfileHosts = new[] { "example.com" }
        };
        var sut = CreateService(settings);
        var request = CreateValidRequest("https://blog.example.com/");

        // Act
        var result = await sut.CreateAuthorizationAsync(request);

        // Assert
        result.Success.Should().BeFalse();
        result.Error.Should().Be("access_denied");
        // Should NOT have proceeded to profile discovery
        _mockProfileDiscovery.Verify(x => x.DiscoverProfileAsync(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task CreateAuthorizationAsync_MultipleHosts_OneMatches_Allowed()
    {
        // Arrange
        var settings = new TalosSettings
        {
            BaseUrl = "https://talos.example.com",
            AllowedProfileHosts = new[] { "foo.com", "bar.com", "example.com" }
        };
        var sut = CreateService(settings);
        SetupSuccessfulProfileDiscovery();
        var request = CreateValidRequest("https://bar.com/user");

        // Act
        var result = await sut.CreateAuthorizationAsync(request);

        // Assert
        result.Error.Should().NotBe("access_denied");
        // Should have proceeded to profile discovery
        _mockProfileDiscovery.Verify(x => x.DiscoverProfileAsync(It.IsAny<string>()), Times.Once);
    }

    [Fact]
    public async Task CreateAuthorizationAsync_InvalidProfileUrl_Denied()
    {
        // Arrange
        var settings = new TalosSettings
        {
            BaseUrl = "https://talos.example.com",
            AllowedProfileHosts = new[] { "example.com" }
        };
        var sut = CreateService(settings);
        var request = CreateValidRequest("not-a-valid-url");

        // Act
        var result = await sut.CreateAuthorizationAsync(request);

        // Assert
        result.Success.Should().BeFalse();
        result.Error.Should().Be("invalid_request",
            "invalid profile URLs are now caught by IsValidProfileUrl before reaching the allowed-hosts check");
        // Should NOT have proceeded to profile discovery
        _mockProfileDiscovery.Verify(x => x.DiscoverProfileAsync(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task CreateAuthorizationAsync_ErrorMessage_DoesNotRevealHost()
    {
        // Arrange
        var settings = new TalosSettings
        {
            BaseUrl = "https://talos.example.com",
            AllowedProfileHosts = new[] { "allowed.com" }
        };
        var sut = CreateService(settings);
        var request = CreateValidRequest("https://denied-host.com/");

        // Act
        var result = await sut.CreateAuthorizationAsync(request);

        // Assert
        result.ErrorDescription.Should().NotContain("denied-host.com");
        result.ErrorDescription.Should().NotContain("allowed.com");
    }
}

