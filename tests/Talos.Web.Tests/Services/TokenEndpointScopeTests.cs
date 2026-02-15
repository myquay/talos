using FluentAssertions;
using Talos.Web.Telemetry;

using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Moq;
using Talos.Web.Configuration;
using Talos.Web.Controllers;
using Talos.Web.Data;
using Talos.Web.Models;
using Talos.Web.Services;

namespace Talos.Web.Tests.Services;

/// <summary>
/// Tests that the token endpoint rejects authorization codes issued with no scope,
/// per IndieAuth spec §5.3.3 (REDEEM-5): "If the authorization code was issued with
/// no scope, the token endpoint MUST NOT issue an access token."
/// Covers GAP-6.
/// </summary>
public class TokenEndpointScopeTests : IDisposable
{
    private readonly Mock<IAuthorizationService> _mockAuthService;
    private readonly Mock<ITokenService> _mockTokenService;
    private readonly TalosDbContext _dbContext;
    private readonly IndieAuthSettings _indieAuthSettings;

    public TokenEndpointScopeTests()
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
            mockSettings.Object,
            new NullAuthTelemetry());
    }

    private void SetupAuthCodeValidation(AuthorizationCode authCode)
    {
        _mockAuthService
            .Setup(x => x.ValidateAuthorizationCodeAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(authCode);
    }

    private static TokenRequest CreateValidTokenRequest()
    {
        return new TokenRequest
        {
            GrantType = "authorization_code",
            Code = "test-code",
            ClientId = "https://app.example.com/",
            RedirectUri = "https://app.example.com/callback",
            CodeVerifier = "dBjftJeZ4CVP-mB92K27uhbUJU1p1r_wW1gFWFOEjXk"
        };
    }

    // ===== Scope-less auth codes rejected at token endpoint =====

    [Fact]
    public async Task Exchange_AuthCodeWithEmptyScopes_ReturnsBadRequest()
    {
        var authCode = new AuthorizationCode
        {
            Code = "test-code",
            ClientId = "https://app.example.com/",
            RedirectUri = "https://app.example.com/callback",
            ProfileUrl = "https://example.com/",
            Scopes = new List<string>(), // empty — authentication-only
            CreatedAt = DateTime.UtcNow,
            ExpiresAt = DateTime.UtcNow.AddMinutes(10)
        };
        SetupAuthCodeValidation(authCode);

        var controller = CreateController();
        var result = await controller.Exchange(CreateValidTokenRequest());

        var badRequest = result.Should().BeOfType<BadRequestObjectResult>().Subject;
        var error = badRequest.Value.Should().BeOfType<TokenErrorResponse>().Subject;
        error.Error.Should().Be("invalid_grant");
        error.ErrorDescription.Should().Contain("no scope");
    }

    [Fact]
    public async Task Exchange_AuthCodeWithNullScopes_ReturnsBadRequest()
    {
        var authCode = new AuthorizationCode
        {
            Code = "test-code",
            ClientId = "https://app.example.com/",
            RedirectUri = "https://app.example.com/callback",
            ProfileUrl = "https://example.com/",
            Scopes = null!, // null edge case
            CreatedAt = DateTime.UtcNow,
            ExpiresAt = DateTime.UtcNow.AddMinutes(10)
        };
        SetupAuthCodeValidation(authCode);

        var controller = CreateController();
        var result = await controller.Exchange(CreateValidTokenRequest());

        var badRequest = result.Should().BeOfType<BadRequestObjectResult>().Subject;
        var error = badRequest.Value.Should().BeOfType<TokenErrorResponse>().Subject;
        error.Error.Should().Be("invalid_grant");
    }

    // ===== Auth codes with scopes succeed at token endpoint =====

    [Fact]
    public async Task Exchange_AuthCodeWithScopes_ReturnsToken()
    {
        var authCode = new AuthorizationCode
        {
            Code = "test-code",
            ClientId = "https://app.example.com/",
            RedirectUri = "https://app.example.com/callback",
            ProfileUrl = "https://example.com/",
            Scopes = new List<string> { "profile" },
            CreatedAt = DateTime.UtcNow,
            ExpiresAt = DateTime.UtcNow.AddMinutes(10)
        };
        SetupAuthCodeValidation(authCode);

        _mockTokenService
            .Setup(x => x.GenerateAccessToken(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<IEnumerable<string>>()))
            .Returns("test-access-token");
        _mockTokenService
            .Setup(x => x.GenerateRefreshToken())
            .Returns("test-refresh-token");

        var controller = CreateController();
        var result = await controller.Exchange(CreateValidTokenRequest());

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var response = ok.Value.Should().BeOfType<TokenResponse>().Subject;
        response.AccessToken.Should().Be("test-access-token");
        response.RefreshToken.Should().Be("test-refresh-token");
        response.Me.Should().Be("https://example.com/");
        response.Scope.Should().Be("profile");
    }

    // ===== Auth endpoint POST still works for scope-less codes =====

    [Fact]
    public async Task VerifyAuthorizationCode_AuthCodeWithNoScope_ReturnsMe()
    {
        // The AUTH endpoint POST should still succeed with an empty-scope code
        // because it only returns the profile URL (no tokens)
        var authCode = new AuthorizationCode
        {
            Code = "test-code",
            ClientId = "https://app.example.com/",
            RedirectUri = "https://app.example.com/callback",
            ProfileUrl = "https://example.com/",
            Scopes = new List<string>(),
            CreatedAt = DateTime.UtcNow,
            ExpiresAt = DateTime.UtcNow.AddMinutes(10)
        };
        SetupAuthCodeValidation(authCode);

        var talosOptions = new Mock<IOptions<TalosSettings>>();
        talosOptions.Setup(x => x.Value).Returns(new TalosSettings { BaseUrl = "https://talos.example.com" });

        var authController = new AuthController(_mockAuthService.Object, talosOptions.Object, new NullAuthTelemetry());
        var result = await authController.VerifyAuthorizationCode(
            "authorization_code", "test-code", "https://app.example.com/",
            "https://app.example.com/callback", "verifier");

        result.Should().BeOfType<OkObjectResult>();
    }

    [Fact]
    public async Task VerifyAuthorizationCode_AuthCodeWithScope_StillReturnsOnlyMe()
    {
        // Auth endpoint POST always returns only "me", even if the code had scopes.
        // Tokens come from the token endpoint, not the auth endpoint.
        var authCode = new AuthorizationCode
        {
            Code = "test-code",
            ClientId = "https://app.example.com/",
            RedirectUri = "https://app.example.com/callback",
            ProfileUrl = "https://example.com/",
            Scopes = new List<string> { "profile", "email" },
            CreatedAt = DateTime.UtcNow,
            ExpiresAt = DateTime.UtcNow.AddMinutes(10)
        };
        SetupAuthCodeValidation(authCode);

        var talosOptions = new Mock<IOptions<TalosSettings>>();
        talosOptions.Setup(x => x.Value).Returns(new TalosSettings { BaseUrl = "https://talos.example.com" });

        var authController = new AuthController(_mockAuthService.Object, talosOptions.Object, new NullAuthTelemetry());
        var result = await authController.VerifyAuthorizationCode(
            "authorization_code", "test-code", "https://app.example.com/",
            "https://app.example.com/callback", "verifier");

        result.Should().BeOfType<OkObjectResult>();
    }
}
