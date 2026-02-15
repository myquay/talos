using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Talos.Web.Configuration;
using Talos.Web.Data;
using Talos.Web.Data.Entities;
using Talos.Web.Models;
using Talos.Web.Services;
using Talos.Web.Services.IdentityProviders;

namespace Talos.Web.Tests.Services;

/// <summary>
/// Tests that the authorization response includes the required `iss` parameter (RESP-3, RESP-5).
/// Spec: https://indieauth.spec.indieweb.org/#authorization-response
/// </summary>
public class AuthorizationServiceIssParameterTests : IDisposable
{
    private readonly TalosDbContext _dbContext;
    private readonly Mock<IProfileDiscoveryService> _mockProfileDiscovery;
    private readonly Mock<IClientDiscoveryService> _mockClientDiscovery;
    private readonly Mock<IPkceService> _mockPkceService;
    private readonly Mock<IIdentityProviderFactory> _mockProviderFactory;
    private readonly IndieAuthSettings _indieAuthSettings;
    private readonly Mock<ILogger<AuthorizationService>> _mockLogger;

    private const string BaseUrl = "https://talos.example.com";

    public AuthorizationServiceIssParameterTests()
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

        _indieAuthSettings = new IndieAuthSettings
        {
            AuthorizationCodeExpirationMinutes = 10,
            RefreshTokenExpirationDays = 30,
            PendingAuthenticationExpirationMinutes = 30
        };
    }

    public void Dispose()
    {
        _dbContext.Dispose();
    }

    private AuthorizationService CreateService(string baseUrl = BaseUrl)
    {
        var talosSettings = new TalosSettings { BaseUrl = baseUrl };
        var talosOptions = new Mock<IOptions<TalosSettings>>();
        talosOptions.Setup(x => x.Value).Returns(talosSettings);

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

    /// <summary>
    /// Seeds a pending authentication that is authenticated and consent-given,
    /// ready for authorization code creation.
    /// </summary>
    private async Task<string> SeedCompletedPendingAuthentication()
    {
        var sessionId = Guid.NewGuid().ToString("N");

        _dbContext.PendingAuthentications.Add(new PendingAuthenticationEntity
        {
            SessionId = sessionId,
            ClientId = "https://app.example.com/",
            RedirectUri = "https://app.example.com/callback",
            State = "test-state-123",
            CodeChallenge = "E9Melhoa2OwvFrEMTJguCHaoeK1t8URWbuGJSstw-cM",
            CodeChallengeMethod = "S256",
            Scopes = "profile create",
            ProfileUrl = "https://user.example.com/",
            IsAuthenticated = true,
            IsConsentGiven = true,
            CreatedAt = DateTime.UtcNow,
            ExpiresAt = DateTime.UtcNow.AddMinutes(30)
        });
        await _dbContext.SaveChangesAsync();

        return sessionId;
    }

    [Fact]
    public async Task CreateAuthorizationCodeAsync_RedirectUrl_ContainsIssParameter()
    {
        // Arrange
        var sessionId = await SeedCompletedPendingAuthentication();
        var sut = CreateService();

        // Act
        var result = await sut.CreateAuthorizationCodeAsync(sessionId);

        // Assert
        result.Success.Should().BeTrue();
        result.RedirectUrl.Should().NotBeNull();
        var uri = new Uri(result.RedirectUrl!);
        var query = System.Web.HttpUtility.ParseQueryString(uri.Query);
        query["iss"].Should().NotBeNullOrEmpty("the iss parameter is required per IndieAuth spec §5.2.1");
    }

    [Fact]
    public async Task CreateAuthorizationCodeAsync_IssParameter_MatchesConfiguredBaseUrl()
    {
        // Arrange
        var sessionId = await SeedCompletedPendingAuthentication();
        var sut = CreateService(baseUrl: "https://talos.example.com");

        // Act
        var result = await sut.CreateAuthorizationCodeAsync(sessionId);

        // Assert
        result.Success.Should().BeTrue();
        var uri = new Uri(result.RedirectUrl!);
        var query = System.Web.HttpUtility.ParseQueryString(uri.Query);
        query["iss"].Should().Be("https://talos.example.com",
            "the iss value must match the issuer in the metadata endpoint");
    }

    [Fact]
    public async Task CreateAuthorizationCodeAsync_IssParameter_TrimsTrailingSlash()
    {
        // Arrange — BaseUrl configured with trailing slash
        var sessionId = await SeedCompletedPendingAuthentication();
        var sut = CreateService(baseUrl: "https://talos.example.com/");

        // Act
        var result = await sut.CreateAuthorizationCodeAsync(sessionId);

        // Assert
        result.Success.Should().BeTrue();
        var uri = new Uri(result.RedirectUrl!);
        var query = System.Web.HttpUtility.ParseQueryString(uri.Query);
        query["iss"].Should().Be("https://talos.example.com",
            "trailing slash should be trimmed to match metadata issuer format");
    }

    [Fact]
    public async Task CreateAuthorizationCodeAsync_RedirectUrl_ContainsAllRequiredParameters()
    {
        // Arrange
        var sessionId = await SeedCompletedPendingAuthentication();
        var sut = CreateService();

        // Act
        var result = await sut.CreateAuthorizationCodeAsync(sessionId);

        // Assert
        result.Success.Should().BeTrue();
        var uri = new Uri(result.RedirectUrl!);
        var query = System.Web.HttpUtility.ParseQueryString(uri.Query);

        query["code"].Should().NotBeNullOrEmpty("code is required per IndieAuth spec §5.2.1");
        query["state"].Should().Be("test-state-123", "state must match the original request value");
        query["iss"].Should().NotBeNullOrEmpty("iss is required per IndieAuth spec §5.2.1 / RFC 9207");
    }
}
