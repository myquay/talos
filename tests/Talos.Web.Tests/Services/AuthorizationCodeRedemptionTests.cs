using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Talos.Web.Configuration;
using Talos.Web.Data;
using Talos.Web.Data.Entities;
using Talos.Web.Services;
using Talos.Web.Services.IdentityProviders;

namespace Talos.Web.Tests.Services;

public class AuthorizationCodeRedemptionTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly TalosDbContext _dbContext;
    private readonly Mock<IPkceService> _mockPkceService = new();

    public AuthorizationCodeRedemptionTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<TalosDbContext>()
            .UseSqlite(_connection)
            .Options;

        _dbContext = new TalosDbContext(options);
        _dbContext.Database.EnsureCreated();

        _mockPkceService
            .Setup(x => x.ValidateCodeVerifier("valid-verifier", "challenge", "S256"))
            .Returns(true);
    }

    public void Dispose()
    {
        _dbContext.Dispose();
        _connection.Dispose();
    }

    private AuthorizationService CreateService()
    {
        var talosOptions = new Mock<IOptions<TalosSettings>>();
        talosOptions.Setup(x => x.Value).Returns(new TalosSettings { BaseUrl = "https://talos.example.com" });

        var indieAuthOptions = new Mock<IOptions<IndieAuthSettings>>();
        indieAuthOptions.Setup(x => x.Value).Returns(new IndieAuthSettings());

        return new AuthorizationService(
            _dbContext,
            Mock.Of<IProfileDiscoveryService>(),
            Mock.Of<IClientDiscoveryService>(),
            _mockPkceService.Object,
            Mock.Of<IIdentityProviderFactory>(),
            indieAuthOptions.Object,
            talosOptions.Object,
            Mock.Of<ILogger<AuthorizationService>>());
    }

    private async Task SeedAuthorizationCodeAsync()
    {
        _dbContext.AuthorizationCodes.Add(new AuthorizationCodeEntity
        {
            Code = "auth-code",
            ClientId = "https://app.example.com/",
            RedirectUri = "https://app.example.com/callback",
            ProfileUrl = "https://user.example.com/",
            Scopes = "create",
            CodeChallenge = "challenge",
            CodeChallengeMethod = "S256",
            CreatedAt = DateTime.UtcNow,
            ExpiresAt = DateTime.UtcNow.AddMinutes(10)
        });
        await _dbContext.SaveChangesAsync();
    }

    [Fact]
    public async Task ValidateAuthorizationCodeAsync_SecondRedemptionFails()
    {
        await SeedAuthorizationCodeAsync();
        var sut = CreateService();

        var first = await sut.ValidateAuthorizationCodeAsync(
            "auth-code",
            "https://app.example.com/",
            "https://app.example.com/callback",
            "valid-verifier");

        var second = await sut.ValidateAuthorizationCodeAsync(
            "auth-code",
            "https://app.example.com/",
            "https://app.example.com/callback",
            "valid-verifier");

        first.Should().NotBeNull();
        second.Should().BeNull("authorization codes are valid for only one successful redemption");
    }
}
