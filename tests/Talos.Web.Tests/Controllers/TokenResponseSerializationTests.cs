using System.Text.Json;
using FluentAssertions;
using Talos.Web.Controllers;

namespace Talos.Web.Tests.Controllers;

public class TokenResponseSerializationTests
{
    [Fact]
    public void TokenResponse_UsesOAuthWireNames()
    {
        var response = new TokenResponse
        {
            AccessToken = "access-token",
            TokenType = "Bearer",
            ExpiresIn = 900,
            RefreshToken = "refresh-token",
            Scope = "profile create",
            Me = "https://example.com/"
        };

        using var document = JsonDocument.Parse(JsonSerializer.Serialize(response));
        var root = document.RootElement;

        root.GetProperty("access_token").GetString().Should().Be("access-token");
        root.GetProperty("token_type").GetString().Should().Be("Bearer");
        root.GetProperty("expires_in").GetInt32().Should().Be(900);
        root.GetProperty("refresh_token").GetString().Should().Be("refresh-token");
        root.GetProperty("scope").GetString().Should().Be("profile create");
        root.GetProperty("me").GetString().Should().Be("https://example.com/");
        root.TryGetProperty("accessToken", out _).Should().BeFalse();
    }

    [Fact]
    public void TokenErrorResponse_UsesOAuthWireNames()
    {
        var response = new TokenErrorResponse
        {
            Error = "invalid_grant",
            ErrorDescription = "The authorization code is invalid."
        };

        using var document = JsonDocument.Parse(JsonSerializer.Serialize(response));
        var root = document.RootElement;

        root.GetProperty("error").GetString().Should().Be("invalid_grant");
        root.GetProperty("error_description").GetString().Should().Be("The authorization code is invalid.");
        root.TryGetProperty("errorDescription", out _).Should().BeFalse();
    }
}
