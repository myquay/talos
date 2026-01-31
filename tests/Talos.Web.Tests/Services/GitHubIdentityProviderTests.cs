using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Talos.Web.Configuration;
using Talos.Web.Services.IdentityProviders;

namespace Talos.Web.Tests.Services;

/// <summary>
/// Tests for GitHubIdentityProvider
/// </summary>
public class GitHubIdentityProviderTests
{
    private readonly GitHubIdentityProvider _sut;

    public GitHubIdentityProviderTests()
    {
        var settings = new GitHubSettings
        {
            ClientId = "test-client-id",
            ClientSecret = "test-client-secret",
            AuthorizationEndpoint = "https://github.com/login/oauth/authorize",
            TokenEndpoint = "https://github.com/login/oauth/access_token",
            UserApiEndpoint = "https://api.github.com/user"
        };

        var mockOptions = new Mock<IOptions<GitHubSettings>>();
        mockOptions.Setup(x => x.Value).Returns(settings);

        var mockFactory = new Mock<IHttpClientFactory>();
        var mockLogger = new Mock<ILogger<GitHubIdentityProvider>>();

        _sut = new GitHubIdentityProvider(mockOptions.Object, mockFactory.Object, mockLogger.Object);
    }

    // ===== Provider Properties =====

    [Fact]
    public void ProviderType_ReturnsGitHub()
    {
        _sut.ProviderType.Should().Be("github");
    }

    [Fact]
    public void DisplayName_ReturnsGitHub()
    {
        _sut.DisplayName.Should().Be("GitHub");
    }

    // ===== URL Pattern Matching Tests =====

    [Theory]
    [InlineData("https://github.com/janedoe")]
    [InlineData("https://github.com/JaneDoe")]
    [InlineData("https://www.github.com/janedoe")]
    [InlineData("https://github.com/user123")]
    [InlineData("https://github.com/user-name")]
    public void CanHandle_ValidGitHubProfileUrl_ReturnsTrue(string url)
    {
        _sut.CanHandle(url).Should().BeTrue();
    }

    [Theory]
    [InlineData("https://github.com/")]                        // No username
    [InlineData("https://github.com/janedoe/repo")]           // Repo path
    [InlineData("https://github.com/orgs/myorg")]             // Org path
    [InlineData("https://github.com/login")]                   // Reserved path
    [InlineData("https://github.com/settings")]                // Reserved path
    [InlineData("https://github.com/explore")]                 // Reserved path
    [InlineData("https://gitlab.com/janedoe")]                 // Wrong domain
    [InlineData("https://twitter.com/janedoe")]                // Wrong domain
    [InlineData("not-a-url")]                                  // Invalid URL
    [InlineData("")]                                           // Empty
    [InlineData("https://github.com/user_name")]               // Underscores not valid in GitHub
    public void CanHandle_InvalidGitHubUrl_ReturnsFalse(string url)
    {
        _sut.CanHandle(url).Should().BeFalse();
    }

    // ===== Authorization URL Generation Tests =====

    [Fact]
    public void GetAuthorizationUrl_ReturnsValidUrl()
    {
        var state = "random-state";
        var redirectUri = "https://talos.example.com/callback/github";

        var authUrl = _sut.GetAuthorizationUrl(state, redirectUri);

        authUrl.Should().StartWith("https://github.com/login/oauth/authorize");
        authUrl.Should().Contain("client_id=test-client-id");
        authUrl.Should().Contain($"state={state}");
        authUrl.Should().Contain("scope=");
    }

    [Fact]
    public void GetAuthorizationUrl_EncodesRedirectUri()
    {
        var state = "test-state";
        var redirectUri = "https://talos.example.com/callback/github?extra=param";

        var authUrl = _sut.GetAuthorizationUrl(state, redirectUri);

        // Should be URL-encoded
        authUrl.Should().Contain("redirect_uri=");
        authUrl.Should().Contain(Uri.EscapeDataString(redirectUri));
    }
}


