using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
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
            var client = _httpClientFactory.CreateClient("GitHub");
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

    public async Task<ProviderVerificationResult> VerifyProfileAsync(string accessToken, string expectedProfileUrl, string userWebsiteUrl)
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
            if (!string.Equals(userInfo.Login, expectedUsername, StringComparison.OrdinalIgnoreCase))
            {
                result.Error = "GitHub username does not match the expected profile";
                return result;
            }

            // Verify reciprocal link - GitHub profile must link back to user's website
            if (!HasReciprocalLink(userInfo, userWebsiteUrl))
            {
                _logger.LogWarning("GitHub profile {Username} does not have a reciprocal link to {Website}", 
                    userInfo.Login, userWebsiteUrl);
                result.Error = "GitHub profile must link back to your website in the blog field or bio";
                return result;
            }

            _logger.LogDebug("Verified reciprocal link from GitHub profile {Username} to {Website}", 
                userInfo.Login, userWebsiteUrl);

            result.Verified = true;
            result.ProfileUrl = userInfo.HtmlUrl;
            result.Name = userInfo.Name ?? userInfo.Login;
            result.Email = userInfo.Email;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error verifying GitHub profile");
            result.Error = "An error occurred during verification";
        }

        return result;
    }

    /// <summary>
    /// Checks if the GitHub profile contains a reciprocal link back to the user's website.
    /// The link can be in the blog field or mentioned in the bio.
    /// </summary>
    private bool HasReciprocalLink(GitHubUserInfo userInfo, string userWebsiteUrl)
    {
        if (string.IsNullOrEmpty(userWebsiteUrl))
            return false;

        // Normalize the user's website URL for comparison
        var websiteHost = GetNormalizedHost(userWebsiteUrl);
        if (string.IsNullOrEmpty(websiteHost))
            return false;

        // Check blog field
        if (!string.IsNullOrEmpty(userInfo.Blog))
        {
            var blogHost = GetNormalizedHost(userInfo.Blog);
            if (string.Equals(blogHost, websiteHost, StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogDebug("Found reciprocal link in GitHub blog field: {Blog}", userInfo.Blog);
                return true;
            }
        }

        // Check bio for URL containing the website host
        if (!string.IsNullOrEmpty(userInfo.Bio))
        {
            // Look for the website URL or host in the bio
            if (userInfo.Bio.Contains(websiteHost, StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogDebug("Found reciprocal link in GitHub bio: {Bio}", userInfo.Bio);
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Extracts and normalizes the host from a URL, handling URLs with or without scheme.
    /// </summary>
    private static string? GetNormalizedHost(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return null;

        // Add scheme if missing (common for blog fields)
        var urlToProcess = url.Trim();
        if (!urlToProcess.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
            !urlToProcess.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            urlToProcess = "https://" + urlToProcess;
        }

        if (Uri.TryCreate(urlToProcess, UriKind.Absolute, out var uri))
        {
            // Remove www. prefix for comparison
            var host = uri.Host;
            if (host.StartsWith("www.", StringComparison.OrdinalIgnoreCase))
            {
                host = host[4..];
            }
            return host.ToLowerInvariant();
        }

        return null;
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
        [JsonPropertyName("access_token")]
        public string? AccessToken { get; set; }

        [JsonPropertyName("token_type")]
        public string? TokenType { get; set; }

        [JsonPropertyName("scope")]
        public string? Scope { get; set; }

        [JsonPropertyName("error")]
        public string? Error { get; set; }

        [JsonPropertyName("error_description")]
        public string? ErrorDescription { get; set; }
    }

    private class GitHubUserInfo
    {
        [JsonPropertyName("id")]
        public long? Id { get; set; }

        [JsonPropertyName("login")]
        public string? Login { get; set; }

        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("email")]
        public string? Email { get; set; }

        [JsonPropertyName("html_url")]
        public string? HtmlUrl { get; set; }

        [JsonPropertyName("blog")]
        public string? Blog { get; set; }

        [JsonPropertyName("bio")]
        public string? Bio { get; set; }
    }
}


