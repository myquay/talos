using FluentAssertions;
using Talos.Web.Telemetry;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Moq;
using Talos.Web.Configuration;
using Talos.Web.Controllers;
using Talos.Web.Data;
using Talos.Web.Services;

namespace Talos.Web.Tests.Controllers;

/// <summary>
/// Tests that the introspection endpoint requires authorization per IndieAuth §6.1
/// and RFC 7662 §2.1. Covers GAP-7.
/// </summary>
public class TokenControllerIntrospectionAuthTests : IDisposable
{
    private const string TestSecret = "test-introspection-secret-abc123";

    private readonly Mock<IAuthorizationService> _mockAuthService;
    private readonly Mock<ITokenService> _mockTokenService;
    private readonly TalosDbContext _dbContext;

    public TokenControllerIntrospectionAuthTests()
    {
        _mockAuthService = new Mock<IAuthorizationService>();
        _mockTokenService = new Mock<ITokenService>();

        var options = new DbContextOptionsBuilder<TalosDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        _dbContext = new TalosDbContext(options);
    }

    public void Dispose()
    {
        _dbContext.Dispose();
    }

    private TokenController CreateController(string? introspectionSecret, string? authorizationHeader = null)
    {
        var mockSettings = new Mock<IOptions<IndieAuthSettings>>();
        mockSettings.Setup(x => x.Value).Returns(new IndieAuthSettings
        {
            AuthorizationCodeExpirationMinutes = 10,
            RefreshTokenExpirationDays = 30,
            IntrospectionSecret = introspectionSecret
        });

        var controller = new TokenController(
            _mockAuthService.Object,
            _mockTokenService.Object,
            _dbContext,
            mockSettings.Object,
            new NullAuthTelemetry());

        // Set up HttpContext so Request.Headers is available
        var httpContext = new DefaultHttpContext();
        if (authorizationHeader != null)
        {
            httpContext.Request.Headers.Authorization = authorizationHeader;
        }
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = httpContext
        };

        return controller;
    }

    // ===== No Authorization header =====

    [Fact]
    public async Task Introspect_NoAuthorizationHeader_Returns401()
    {
        var controller = CreateController(TestSecret, authorizationHeader: null);

        var result = await controller.Introspect("some-token");

        result.Should().BeOfType<UnauthorizedObjectResult>();
    }

    [Fact]
    public async Task Introspect_NoAuthorizationHeader_DoesNotLeakTokenInfo()
    {
        // Even if the token is valid, 401 should be returned without any token metadata
        _mockTokenService
            .Setup(x => x.ValidateAccessTokenAsync(It.IsAny<string>()))
            .ReturnsAsync(new TokenValidationResult
            {
                IsValid = true,
                ProfileUrl = "https://example.com/",
                ClientId = "https://app.example.com/",
                Scopes = new[] { "profile" },
                ExpiresAt = DateTime.UtcNow.AddMinutes(15)
            });

        var controller = CreateController(TestSecret, authorizationHeader: null);
        var result = await controller.Introspect("valid-token");

        result.Should().BeOfType<UnauthorizedObjectResult>();
        // Token service should never have been called
        _mockTokenService.Verify(
            x => x.ValidateAccessTokenAsync(It.IsAny<string>()), Times.Never);
    }

    // ===== Wrong credentials =====

    [Fact]
    public async Task Introspect_WrongBearerToken_Returns401()
    {
        var controller = CreateController(TestSecret, "Bearer wrong-secret");

        var result = await controller.Introspect("some-token");

        result.Should().BeOfType<UnauthorizedObjectResult>();
    }

    [Fact]
    public async Task Introspect_BasicAuthInsteadOfBearer_Returns401()
    {
        var controller = CreateController(TestSecret, "Basic dXNlcjpwYXNz");

        var result = await controller.Introspect("some-token");

        result.Should().BeOfType<UnauthorizedObjectResult>();
    }

    [Fact]
    public async Task Introspect_EmptyBearerToken_Returns401()
    {
        var controller = CreateController(TestSecret, "Bearer ");

        var result = await controller.Introspect("some-token");

        result.Should().BeOfType<UnauthorizedObjectResult>();
    }

    [Fact]
    public async Task Introspect_BearerKeywordOnly_Returns401()
    {
        var controller = CreateController(TestSecret, "Bearer");

        var result = await controller.Introspect("some-token");

        result.Should().BeOfType<UnauthorizedObjectResult>();
    }

    // ===== No secret configured (fail-closed) =====

    [Fact]
    public async Task Introspect_NoSecretConfigured_Returns401()
    {
        var controller = CreateController(
            introspectionSecret: null,
            authorizationHeader: "Bearer anything");

        var result = await controller.Introspect("some-token");

        result.Should().BeOfType<UnauthorizedObjectResult>();
    }

    [Fact]
    public async Task Introspect_EmptySecretConfigured_Returns401()
    {
        var controller = CreateController(
            introspectionSecret: "",
            authorizationHeader: "Bearer anything");

        var result = await controller.Introspect("some-token");

        result.Should().BeOfType<UnauthorizedObjectResult>();
    }

    // ===== Valid credentials =====

    [Fact]
    public async Task Introspect_ValidBearerToken_ValidAccessToken_ReturnsActive()
    {
        _mockTokenService
            .Setup(x => x.ValidateAccessTokenAsync("valid-access-token"))
            .ReturnsAsync(new TokenValidationResult
            {
                IsValid = true,
                ProfileUrl = "https://example.com/",
                ClientId = "https://app.example.com/",
                Scopes = new[] { "profile" },
                ExpiresAt = DateTime.UtcNow.AddMinutes(15)
            });

        var controller = CreateController(TestSecret, $"Bearer {TestSecret}");
        var result = await controller.Introspect("valid-access-token");

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var response = ok.Value.Should().BeOfType<IntrospectionResponse>().Subject;
        response.Active.Should().BeTrue();
        response.Me.Should().Be("https://example.com/");
    }

    [Fact]
    public async Task Introspect_ValidBearerToken_InvalidAccessToken_ReturnsInactive()
    {
        _mockTokenService
            .Setup(x => x.ValidateAccessTokenAsync(It.IsAny<string>()))
            .ReturnsAsync(new TokenValidationResult { IsValid = false });

        var controller = CreateController(TestSecret, $"Bearer {TestSecret}");
        var result = await controller.Introspect("invalid-access-token");

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var response = ok.Value.Should().BeOfType<IntrospectionResponse>().Subject;
        response.Active.Should().BeFalse();
    }

    [Fact]
    public async Task Introspect_ValidBearerToken_EmptyAccessToken_ReturnsInactive()
    {
        var controller = CreateController(TestSecret, $"Bearer {TestSecret}");
        var result = await controller.Introspect("");

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var response = ok.Value.Should().BeOfType<IntrospectionResponse>().Subject;
        response.Active.Should().BeFalse();
    }
}
