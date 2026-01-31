using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using Talos.Web.Configuration;

namespace Talos.Web.Services.IdentityProviders;

public class GitHubIdentityProvider : IIdentityProvider
{
    private readonly GitHubSettings _settings;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<GitHubIdentityProvider> _logger;

    public string ProviderType => "github";
    public string DisplayName => "GitHub";

    private static readonly Regex GitHubUrlPattern = new(
        @"^https?://(?:www\.)?github\.com/([a-zA-Z0-9](?:[a-zA-Z0-9]|-(?=[a-zA-Z0-9])){0,38})/?$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // Reserved GitHub paths that are not user profiles
    private static readonly HashSet<string> ReservedPaths = new(StringComparer.OrdinalIgnoreCase)
    {
        "login", "logout", "join", "settings", "organizations",
        "explore", "trending", "collections", "events", "sponsors",
        "features", "security", "team", "enterprise", "pricing",
        "about", "contact", "orgs", "marketplace", "apps",
        "notifications", "pulls", "issues", "codespaces",
        "discussions", "actions", "projects", "packages"
    };

    public GitHubIdentityProvider(
        IOptions<GitHubSettings> settings,
        IHttpClientFactory httpClientFactory,
        ILogger<GitHubIdentityProvider> logger)
    {
        _settings = settings.Value;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public bool CanHandle(string url)
    {
        var match = GitHubUrlPattern.Match(url);
        if (!match.Success)
            return false;
        
        // Check if the matched username is a reserved path
        var username = match.Groups[1].Value;
        return !ReservedPaths.Contains(username);
    }

    public string GetAuthorizationUrl(string state, string redirectUri)
    {
        var queryParams = new Dictionary<string, string>
        {
            ["client_id"] = _settings.ClientId,
            ["redirect_uri"] = redirectUri,
            ["scope"] = "read:user user:email",
            ["state"] = state,
            ["allow_signup"] = "false"
        };

        var queryString = string.Join("&", 
            queryParams.Select(kvp => $"{kvp.Key}={Uri.EscapeDataString(kvp.Value)}"));
        
        return $"{_settings.AuthorizationEndpoint}?{queryString}";
    }

    public async Task<ProviderAuthResult> ExchangeCodeAsync(string code, string redirectUri)
    {
        var result = new ProviderAuthResult();

        try
        {
            using var client = new HttpClient();
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            var content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["client_id"] = _settings.ClientId,
                ["client_secret"] = _settings.ClientSecret,
                ["code"] = code,
                ["redirect_uri"] = redirectUri
            });

            var response = await client.PostAsync(_settings.TokenEndpoint, content);
            var responseBody = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError("GitHub token exchange failed: {Response}", responseBody);
                result.Error = "Failed to exchange code for token";
                return result;
            }

            var tokenResponse = JsonSerializer.Deserialize<GitHubTokenResponse>(responseBody);
            if (tokenResponse?.AccessToken == null)
            {
                result.Error = tokenResponse?.Error ?? "No access token received";
                return result;
            }

            result.AccessToken = tokenResponse.AccessToken;
            result.Success = true;

            // Fetch user info
            var userInfo = await GetUserInfoAsync(tokenResponse.AccessToken);
            if (userInfo != null)
            {
                result.ProviderUserId = userInfo.Id?.ToString();
                result.ProviderUsername = userInfo.Login;
                result.ProviderProfileUrl = userInfo.HtmlUrl;
                result.Email = userInfo.Email;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error exchanging GitHub code");
            result.Error = "An error occurred during authentication";
        }

        return result;
    }

    public async Task<ProviderVerificationResult> VerifyProfileAsync(string accessToken, string expectedProfileUrl)
    {
        var result = new ProviderVerificationResult();

        try
        {
            var userInfo = await GetUserInfoAsync(accessToken);
            if (userInfo == null)
            {
                result.Error = "Failed to retrieve user info from GitHub";
                return result;
            }

            // Extract expected username from the profile URL
            var match = GitHubUrlPattern.Match(expectedProfileUrl);
            if (!match.Success)
            {
                result.Error = "Invalid GitHub profile URL";
                return result;
            }

            var expectedUsername = match.Groups[1].Value;

            // Verify the GitHub username matches
            if (string.Equals(userInfo.Login, expectedUsername, StringComparison.OrdinalIgnoreCase))
            {
                result.Verified = true;
                result.ProfileUrl = userInfo.HtmlUrl;
                result.Name = userInfo.Name ?? userInfo.Login;
                result.Email = userInfo.Email;

                // Check for reciprocal link in user's bio or blog
                if (!string.IsNullOrEmpty(userInfo.Blog) || !string.IsNullOrEmpty(userInfo.Bio))
                {
                    _logger.LogDebug("GitHub user has blog: {Blog}, bio: {Bio}", userInfo.Blog, userInfo.Bio);
                }
            }
            else
            {
                result.Error = "GitHub username does not match the expected profile";
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error verifying GitHub profile");
            result.Error = "An error occurred during verification";
        }

        return result;
    }

    private async Task<GitHubUserInfo?> GetUserInfoAsync(string accessToken)
    {
        var client = _httpClientFactory.CreateClient("GitHub");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        var response = await client.GetAsync("user");
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogError("Failed to get GitHub user info: {StatusCode}", response.StatusCode);
            return null;
        }

        var json = await response.Content.ReadAsStringAsync();
        return JsonSerializer.Deserialize<GitHubUserInfo>(json, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
        });
    }

    private class GitHubTokenResponse
    {
        public string? AccessToken { get; set; }
        public string? TokenType { get; set; }
        public string? Scope { get; set; }
        public string? Error { get; set; }
        public string? ErrorDescription { get; set; }
    }

    private class GitHubUserInfo
    {
        public long? Id { get; set; }
        public string? Login { get; set; }
        public string? Name { get; set; }
        public string? Email { get; set; }
        public string? HtmlUrl { get; set; }
        public string? Blog { get; set; }
        public string? Bio { get; set; }
    }
}


