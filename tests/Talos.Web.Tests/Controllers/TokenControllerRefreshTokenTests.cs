using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Moq;
using Talos.Web.Configuration;
using Talos.Web.Controllers;
using Talos.Web.Data;
using Talos.Web.Data.Entities;
using Talos.Web.Services;

namespace Talos.Web.Tests.Controllers;

/// <summary>
/// Tests that the refresh token grant requires client_id per IndieAuth spec §5.3.3
/// (REFRESH-2 / GAP-13). The client_id MUST be provided and MUST match the
/// client the refresh token was originally issued to.
/// </summary>
public class TokenControllerRefreshTokenTests : IDisposable
{
    private readonly Mock<IAuthorizationService> _mockAuthService;
    private readonly Mock<ITokenService> _mockTokenService;
    private readonly TalosDbContext _dbContext;
    private readonly IndieAuthSettings _indieAuthSettings;

    private const string ValidRefreshToken = "valid-refresh-token";
    private const string ValidClientId = "https://app.example.com/";

    public TokenControllerRefreshTokenTests()
    {
        _mockAuthService = new Mock<IAuthorizationService>();
        _mockTokenService = new Mock<ITokenService>();

        var options = new DbContextOptionsBuilder<TalosDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        _dbContext = new TalosDbContext(options);

        _indieAuthSettings = new IndieAuthSettings
        {
            AuthorizationCodeExpirationMinutes = 10,
            RefreshTokenExpirationDays = 30,
            PendingAuthenticationExpirationMinutes = 30
        };

        _mockTokenService.Setup(x => x.GenerateRefreshToken())
            .Returns("new-refresh-token");
        _mockTokenService.Setup(x => x.GenerateAccessToken(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<IEnumerable<string>>()))
            .Returns("new-access-token");
    }

    public void Dispose()
    {
        _dbContext.Dispose();
    }

    private TokenController CreateController()
    {
        var mockSettings = new Mock<IOptions<IndieAuthSettings>>();
        mockSettings.Setup(x => x.Value).Returns(_indieAuthSettings);

        return new TokenController(
            _mockAuthService.Object,
            _mockTokenService.Object,
            _dbContext,
            mockSettings.Object);
    }

    private void SeedValidRefreshToken(string clientId = ValidClientId)
    {
        _dbContext.RefreshTokens.Add(new RefreshTokenEntity
        {
            Token = ValidRefreshToken,
            ProfileUrl = "https://user.example.com/",
            ClientId = clientId,
            Scopes = "profile email",
            CreatedAt = DateTime.UtcNow,
            ExpiresAt = DateTime.UtcNow.AddDays(30)
        });
        _dbContext.SaveChanges();
    }

    // ===== client_id required =====

    [Fact]
    public async Task RefreshTokenGrant_MissingClientId_ReturnsError()
    {
        SeedValidRefreshToken();
        var controller = CreateController();

        var result = await controller.Exchange(new TokenRequest
        {
            GrantType = "refresh_token",
            RefreshToken = ValidRefreshToken,
            ClientId = null
        });

        var badRequest = result.Should().BeOfType<BadRequestObjectResult>().Subject;
        var error = badRequest.Value.Should().BeOfType<TokenErrorResponse>().Subject;
        error.Error.Should().Be("invalid_request");
        error.ErrorDescription.Should().Contain("client_id");
    }

    [Fact]
    public async Task RefreshTokenGrant_EmptyClientId_ReturnsError()
    {
        SeedValidRefreshToken();
        var controller = CreateController();

        var result = await controller.Exchange(new TokenRequest
        {
            GrantType = "refresh_token",
            RefreshToken = ValidRefreshToken,
            ClientId = ""
        });

        var badRequest = result.Should().BeOfType<BadRequestObjectResult>().Subject;
        var error = badRequest.Value.Should().BeOfType<TokenErrorResponse>().Subject;
        error.Error.Should().Be("invalid_request");
        error.ErrorDescription.Should().Contain("client_id");
    }

    // ===== client_id mismatch =====

    [Fact]
    public async Task RefreshTokenGrant_WrongClientId_ReturnsError()
    {
        SeedValidRefreshToken("https://app.example.com/");
        var controller = CreateController();

        var result = await controller.Exchange(new TokenRequest
        {
            GrantType = "refresh_token",
            RefreshToken = ValidRefreshToken,
            ClientId = "https://other-app.example.com/"
        });

        var badRequest = result.Should().BeOfType<BadRequestObjectResult>().Subject;
        var error = badRequest.Value.Should().BeOfType<TokenErrorResponse>().Subject;
        error.Error.Should().Be("invalid_grant");
        error.ErrorDescription.Should().Contain("client_id mismatch");
    }

    // ===== correct client_id succeeds =====

    [Fact]
    public async Task RefreshTokenGrant_CorrectClientId_ReturnsNewTokens()
    {
        SeedValidRefreshToken();
        var controller = CreateController();

        var result = await controller.Exchange(new TokenRequest
        {
            GrantType = "refresh_token",
            RefreshToken = ValidRefreshToken,
            ClientId = ValidClientId
        });

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var response = ok.Value.Should().BeOfType<TokenResponse>().Subject;
        response.AccessToken.Should().NotBeNullOrEmpty();
        response.RefreshToken.Should().NotBeNullOrEmpty();
    }

    // ===== refresh token rotation =====

    [Fact]
    public async Task RefreshTokenGrant_Success_RevokesOldToken()
    {
        SeedValidRefreshToken();
        var controller = CreateController();

        await controller.Exchange(new TokenRequest
        {
            GrantType = "refresh_token",
            RefreshToken = ValidRefreshToken,
            ClientId = ValidClientId
        });

        var old = await _dbContext.RefreshTokens.FirstAsync(t => t.Token == ValidRefreshToken);
        old.IsRevoked.Should().BeTrue("the old refresh token should be revoked after rotation");
    }

    // ===== invalid/expired refresh token =====

    [Fact]
    public async Task RefreshTokenGrant_InvalidToken_ReturnsError()
    {
        var controller = CreateController();

        var result = await controller.Exchange(new TokenRequest
        {
            GrantType = "refresh_token",
            RefreshToken = "nonexistent-token",
            ClientId = ValidClientId
        });

        var badRequest = result.Should().BeOfType<BadRequestObjectResult>().Subject;
        var error = badRequest.Value.Should().BeOfType<TokenErrorResponse>().Subject;
        error.Error.Should().Be("invalid_grant");
    }

    // ===== missing refresh_token =====

    [Fact]
    public async Task RefreshTokenGrant_MissingRefreshToken_ReturnsError()
    {
        var controller = CreateController();

        var result = await controller.Exchange(new TokenRequest
        {
            GrantType = "refresh_token",
            RefreshToken = null,
            ClientId = ValidClientId
        });

        var badRequest = result.Should().BeOfType<BadRequestObjectResult>().Subject;
        var error = badRequest.Value.Should().BeOfType<TokenErrorResponse>().Subject;
        error.Error.Should().Be("invalid_request");
        error.ErrorDescription.Should().Contain("refresh_token");
    }
}
