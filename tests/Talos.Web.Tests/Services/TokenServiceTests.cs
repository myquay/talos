using FluentAssertions;
using Microsoft.Extensions.Options;
using Talos.Web.Configuration;
using Talos.Web.Services;
using Moq;
using Microsoft.Extensions.Logging;

namespace Talos.Web.Tests.Services;

/// <summary>
/// Tests for TokenService
/// </summary>
public class TokenServiceTests
{
    private readonly TokenService _sut;
    private readonly JwtSettings _jwtSettings;

    public TokenServiceTests()
    {
        _jwtSettings = new JwtSettings
        {
            Issuer = "https://talos.example.com/",
            Audience = "https://talos.example.com/",
            SecretKey = "test-secret-key-minimum-32-characters-long",
            AccessTokenExpirationMinutes = 15
        };

        var mockOptions = new Mock<IOptions<JwtSettings>>();
        mockOptions.Setup(x => x.Value).Returns(_jwtSettings);

        var mockLogger = new Mock<ILogger<TokenService>>();

        _sut = new TokenService(mockOptions.Object, mockLogger.Object);
    }

    // ===== Access Token Generation Tests =====

    [Fact]
    public void GenerateAccessToken_ReturnsValidJwt()
    {
        var profileUrl = "https://example.com/";
        var clientId = "https://app.example.com/";
        var scopes = new[] { "profile", "email" };

        var token = _sut.GenerateAccessToken(profileUrl, clientId, scopes);

        token.Should().NotBeNullOrEmpty();
        token.Split('.').Should().HaveCount(3); // JWT has 3 parts
    }

    [Fact]
    public async Task GenerateAccessToken_CanBeValidated()
    {
        var profileUrl = "https://example.com/";
        var clientId = "https://app.example.com/";
        var scopes = new[] { "profile", "email" };

        var token = _sut.GenerateAccessToken(profileUrl, clientId, scopes);
        var result = await _sut.ValidateAccessTokenAsync(token);

        result.IsValid.Should().BeTrue();
        result.ProfileUrl.Should().Be(profileUrl);
        result.ClientId.Should().Be(clientId);
        result.Scopes.Should().Contain("profile");
        result.Scopes.Should().Contain("email");
    }

    [Fact]
    public async Task ValidateAccessToken_InvalidToken_ReturnsInvalid()
    {
        var result = await _sut.ValidateAccessTokenAsync("invalid-token");

        result.IsValid.Should().BeFalse();
        result.Error.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task ValidateAccessToken_WrongSigningKey_ReturnsInvalid()
    {
        // Create a token with different settings
        var otherSettings = new JwtSettings
        {
            Issuer = _jwtSettings.Issuer,
            Audience = _jwtSettings.Audience,
            SecretKey = "different-secret-key-minimum-32-chars",
            AccessTokenExpirationMinutes = 15
        };
        var mockOtherOptions = new Mock<IOptions<JwtSettings>>();
        mockOtherOptions.Setup(x => x.Value).Returns(otherSettings);
        var otherService = new TokenService(mockOtherOptions.Object, new Mock<ILogger<TokenService>>().Object);

        var token = otherService.GenerateAccessToken("https://example.com/", "https://app.example.com/", new[] { "profile" });
        var result = await _sut.ValidateAccessTokenAsync(token);

        result.IsValid.Should().BeFalse();
    }

    // ===== Refresh Token Generation Tests =====

    [Fact]
    public void GenerateRefreshToken_ReturnsRandomToken()
    {
        var token1 = _sut.GenerateRefreshToken();
        var token2 = _sut.GenerateRefreshToken();

        token1.Should().NotBeNullOrEmpty();
        token2.Should().NotBeNullOrEmpty();
        token1.Should().NotBe(token2);
    }

    [Fact]
    public void GenerateRefreshToken_IsBase64Encoded()
    {
        var token = _sut.GenerateRefreshToken();

        // Should be able to decode as base64
        var action = () => Convert.FromBase64String(token);
        action.Should().NotThrow();
    }
}

