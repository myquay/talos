using FluentAssertions;
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
/// Controller-level tests for the token introspection endpoint.
/// Verifies the Introspect action returns IntrospectionResponse objects
/// with correct Active flag values.
/// </summary>
public class TokenControllerIntrospectionTests : IDisposable
{
    private readonly Mock<IAuthorizationService> _mockAuthService;
    private readonly Mock<ITokenService> _mockTokenService;
    private readonly TalosDbContext _dbContext;

    public TokenControllerIntrospectionTests()
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

    private TokenController CreateController()
    {
        var mockSettings = new Mock<IOptions<IndieAuthSettings>>();
        mockSettings.Setup(x => x.Value).Returns(new IndieAuthSettings
        {
            AuthorizationCodeExpirationMinutes = 10,
            RefreshTokenExpirationDays = 30
        });

        return new TokenController(
            _mockAuthService.Object,
            _mockTokenService.Object,
            _dbContext,
            mockSettings.Object);
    }

    [Fact]
    public async Task Introspect_EmptyToken_ReturnsInactiveResponse()
    {
        var controller = CreateController();

        var result = await controller.Introspect("");

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var response = ok.Value.Should().BeOfType<IntrospectionResponse>().Subject;
        response.Active.Should().BeFalse();
        response.Me.Should().BeNull();
        response.ClientId.Should().BeNull();
    }

    [Fact]
    public async Task Introspect_InvalidToken_ReturnsInactiveResponse()
    {
        _mockTokenService
            .Setup(x => x.ValidateAccessTokenAsync(It.IsAny<string>()))
            .ReturnsAsync(new TokenValidationResult { IsValid = false });

        var controller = CreateController();
        var result = await controller.Introspect("invalid-token");

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var response = ok.Value.Should().BeOfType<IntrospectionResponse>().Subject;
        response.Active.Should().BeFalse();
    }

    [Fact]
    public async Task Introspect_ValidToken_ReturnsActiveResponse()
    {
        var expiresAt = DateTime.UtcNow.AddMinutes(15);
        _mockTokenService
            .Setup(x => x.ValidateAccessTokenAsync("valid-token"))
            .ReturnsAsync(new TokenValidationResult
            {
                IsValid = true,
                ProfileUrl = "https://example.com/",
                ClientId = "https://app.example.com/",
                Scopes = new[] { "profile", "email" },
                ExpiresAt = expiresAt
            });

        var controller = CreateController();
        var result = await controller.Introspect("valid-token");

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var response = ok.Value.Should().BeOfType<IntrospectionResponse>().Subject;
        response.Active.Should().BeTrue();
        response.Me.Should().Be("https://example.com/");
        response.ClientId.Should().Be("https://app.example.com/");
        response.Scope.Should().Be("profile email");
        response.Exp.Should().Be(new DateTimeOffset(expiresAt).ToUnixTimeSeconds());
    }
}
