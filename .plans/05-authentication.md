# 05 - Authentication (Identity Providers)

## Implementation Status: ✅ COMPLETED (January 31, 2026)

### What Was Implemented
- GitHubIdentityProvider with full OAuth 2.0 flow support
- URL pattern matching for GitHub profile URLs (github.com/username)
- OAuth authorization URL generation with proper scopes (read:user, user:email)
- Code-to-token exchange with GitHub's OAuth endpoint
- User profile verification (username matching)
- Reciprocal link verification (checking GitHub blog/bio for user's website URL)
- CallbackController handling GitHub OAuth callbacks
- Provider state management via PendingAuthentication entity
- Error handling for OAuth errors, token exchange failures, and verification failures
- IdentityProviderFactory for extensible provider architecture

---

## Overview

Talos authenticates users via third-party identity providers discovered from the user's website. This document covers the GitHub OAuth integration and the extensible identity provider architecture.

## Design Principles

1. **No User Database**: Users are not stored - authentication is delegated to identity providers
2. **RelMeAuth Verification**: Identity is verified by matching `rel="me"` links bidirectionally
3. **Extensible Providers**: Easy to add new identity providers (Twitter, Mastodon, etc.)
4. **Session-less Flow**: All state is stored in pending authentication records

## Authentication Flow

```
┌──────────────┐     ┌──────────────┐     ┌──────────────┐     ┌──────────────┐
│  User's      │     │    Talos     │     │   GitHub     │     │   GitHub     │
│  Website     │     │   Server     │     │   OAuth      │     │   API        │
└──────┬───────┘     └──────┬───────┘     └──────┬───────┘     └──────┬───────┘
       │                    │                    │                    │
       │ 1. Fetch profile   │                    │                    │
       │◄───────────────────│                    │                    │
       │                    │                    │                    │
       │ 2. Return rel="me" │                    │                    │
       │    to github.com/x │                    │                    │
       │───────────────────►│                    │                    │
       │                    │                    │                    │
       │                    │ 3. Redirect to GitHub OAuth             │
       │                    │───────────────────►│                    │
       │                    │                    │                    │
       │                    │ 4. User authenticates                   │
       │                    │◄───────────────────│                    │
       │                    │                    │                    │
       │                    │ 5. Exchange code for token              │
       │                    │───────────────────►│                    │
       │                    │                    │                    │
       │                    │ 6. Token received  │                    │
       │                    │◄───────────────────│                    │
       │                    │                    │                    │
       │                    │ 7. Fetch GitHub user profile            │
       │                    │────────────────────┼───────────────────►│
       │                    │                    │                    │
       │                    │ 8. Verify username matches rel="me"     │
       │                    │◄───────────────────┼────────────────────│
       │                    │                    │                    │
       │                    │ 9. Check reciprocal link               │
       │                    │    (GitHub profile → user's site)       │
       │                    │                    │                    │
       │                    │ 10. User verified as their website URL  │
       │                    │                    │                    │
```

## Identity Provider Architecture

### Interface

```csharp
public interface IIdentityProvider
{
    /// <summary>
    /// Unique identifier (e.g., "github", "twitter")
    /// </summary>
    string ProviderType { get; }
    
    /// <summary>
    /// Human-readable name (e.g., "GitHub")
    /// </summary>
    string DisplayName { get; }
    
    /// <summary>
    /// Provider icon URL
    /// </summary>
    string? IconUrl { get; }

    /// <summary>
    /// Check if a URL matches this provider's profile pattern
    /// Returns username if match, null otherwise
    /// </summary>
    ProviderMatch? MatchProfileUrl(string url);

    /// <summary>
    /// Build OAuth authorization URL
    /// </summary>
    string GetAuthorizationUrl(string state, string redirectUri);

    /// <summary>
    /// Exchange authorization code for access token
    /// </summary>
    Task<ProviderTokenResult> ExchangeCodeAsync(string code, string redirectUri);

    /// <summary>
    /// Verify authenticated user matches expected profile
    /// </summary>
    Task<ProviderVerificationResult> VerifyAsync(
        string accessToken,
        string expectedUsername,
        string userWebsiteUrl);
}
```

### Provider Factory

```csharp
public interface IIdentityProviderFactory
{
    IIdentityProvider? GetProvider(string providerType);
    IEnumerable<IIdentityProvider> GetAllProviders();
}

public class IdentityProviderFactory : IIdentityProviderFactory
{
    private readonly Dictionary<string, IIdentityProvider> _providers;

    public IdentityProviderFactory(IEnumerable<IIdentityProvider> providers)
    {
        _providers = providers.ToDictionary(p => p.ProviderType);
    }

    public IIdentityProvider? GetProvider(string providerType)
    {
        return _providers.TryGetValue(providerType, out var provider) ? provider : null;
    }

    public IEnumerable<IIdentityProvider> GetAllProviders() => _providers.Values;
}

// Registration in Program.cs
builder.Services.AddSingleton<IIdentityProvider, GitHubIdentityProvider>();
builder.Services.AddSingleton<IIdentityProviderFactory, IdentityProviderFactory>();
```

## GitHub Identity Provider

### Configuration

```json
{
  "GitHub": {
    "ClientId": "your-github-client-id",
    "ClientSecret": "your-github-client-secret",
    "AuthorizationEndpoint": "https://github.com/login/oauth/authorize",
    "TokenEndpoint": "https://github.com/login/oauth/access_token",
    "UserApiEndpoint": "https://api.github.com/user"
  }
}
```

### Implementation

```csharp
public class GitHubIdentityProvider : IIdentityProvider
{
    private readonly GitHubSettings _settings;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<GitHubIdentityProvider> _logger;

    public string ProviderType => "github";
    public string DisplayName => "GitHub";
    public string? IconUrl => "/images/github-mark.svg";

    public GitHubIdentityProvider(
        IOptions<GitHubSettings> settings,
        IHttpClientFactory httpClientFactory,
        ILogger<GitHubIdentityProvider> logger)
    {
        _settings = settings.Value;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    /// <summary>
    /// Match GitHub profile URLs
    /// Examples:
    ///   https://github.com/janedoe → janedoe
    ///   https://www.github.com/janedoe → janedoe
    /// </summary>
    public ProviderMatch? MatchProfileUrl(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            return null;

        // Must be github.com
        var host = uri.Host.ToLowerInvariant();
        if (host != "github.com" && host != "www.github.com")
            return null;

        // Extract username from path
        var path = uri.AbsolutePath.Trim('/');
        
        // Must be just username (no slashes = not a repo)
        if (string.IsNullOrEmpty(path) || path.Contains('/'))
            return null;

        // Exclude reserved GitHub paths
        var reserved = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "login", "logout", "join", "settings", "organizations",
            "explore", "trending", "collections", "events", "sponsors",
            "features", "security", "team", "enterprise", "pricing",
            "about", "contact", "topics", "codespaces", "issues",
            "pulls", "marketplace", "notifications", "new"
        };
        
        if (reserved.Contains(path))
            return null;

        return new ProviderMatch { Username = path };
    }

    /// <summary>
    /// Build GitHub OAuth authorization URL
    /// </summary>
    public string GetAuthorizationUrl(string state, string redirectUri)
    {
        var queryParams = new Dictionary<string, string>
        {
            ["client_id"] = _settings.ClientId,
            ["redirect_uri"] = redirectUri,
            ["state"] = state,
            ["scope"] = "read:user"  // Only need to read profile
        };

        var query = string.Join("&", queryParams
            .Select(kvp => $"{kvp.Key}={Uri.EscapeDataString(kvp.Value)}"));

        return $"{_settings.AuthorizationEndpoint}?{query}";
    }

    /// <summary>
    /// Exchange authorization code for access token
    /// </summary>
    public async Task<ProviderTokenResult> ExchangeCodeAsync(string code, string redirectUri)
    {
        try
        {
            var client = _httpClientFactory.CreateClient();
            
            var request = new HttpRequestMessage(HttpMethod.Post, _settings.TokenEndpoint);
            request.Headers.Accept.Add(
                new MediaTypeWithQualityHeaderValue("application/json"));
            
            request.Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["client_id"] = _settings.ClientId,
                ["client_secret"] = _settings.ClientSecret,
                ["code"] = code,
                ["redirect_uri"] = redirectUri
            });

            var response = await client.SendAsync(request);
            var content = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("GitHub token exchange failed: {Status} {Content}", 
                    response.StatusCode, content);
                return new ProviderTokenResult 
                { 
                    Success = false, 
                    Error = "Failed to exchange authorization code" 
                };
            }

            var tokenResponse = JsonSerializer.Deserialize<GitHubTokenResponse>(content);
            
            if (!string.IsNullOrEmpty(tokenResponse?.Error))
            {
                _logger.LogWarning("GitHub token error: {Error}", tokenResponse.Error);
                return new ProviderTokenResult 
                { 
                    Success = false, 
                    Error = tokenResponse.ErrorDescription ?? tokenResponse.Error 
                };
            }

            return new ProviderTokenResult
            {
                Success = true,
                AccessToken = tokenResponse?.AccessToken
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error exchanging GitHub code");
            return new ProviderTokenResult 
            { 
                Success = false, 
                Error = "Failed to exchange authorization code" 
            };
        }
    }

    /// <summary>
    /// Verify authenticated user and check reciprocal link
    /// </summary>
    public async Task<ProviderVerificationResult> VerifyAsync(
        string accessToken,
        string expectedUsername,
        string userWebsiteUrl)
    {
        try
        {
            // Fetch GitHub user profile
            var client = _httpClientFactory.CreateClient("GitHub");
            client.DefaultRequestHeaders.Authorization = 
                new AuthenticationHeaderValue("Bearer", accessToken);

            var response = await client.GetAsync("user");
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("GitHub user fetch failed: {Status}", response.StatusCode);
                return new ProviderVerificationResult
                {
                    Success = false,
                    Error = "Failed to fetch GitHub user profile"
                };
            }

            var content = await response.Content.ReadAsStringAsync();
            var user = JsonSerializer.Deserialize<GitHubUser>(content);

            if (user == null)
            {
                return new ProviderVerificationResult
                {
                    Success = false,
                    Error = "Invalid GitHub user response"
                };
            }

            // Verify username matches what we expected from rel="me"
            if (!string.Equals(user.Login, expectedUsername, StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogWarning(
                    "GitHub username mismatch. Expected: {Expected}, Got: {Actual}",
                    expectedUsername, user.Login);
                    
                return new ProviderVerificationResult
                {
                    Success = false,
                    Error = $"GitHub username mismatch. Expected {expectedUsername}, got {user.Login}"
                };
            }

            // Check for reciprocal link (GitHub profile → user's website)
            var reciprocalVerified = CheckReciprocalLink(user, userWebsiteUrl);

            return new ProviderVerificationResult
            {
                Success = true,
                Username = user.Login,
                ProfileUrl = user.HtmlUrl,
                DisplayName = user.Name ?? user.Login,
                AvatarUrl = user.AvatarUrl,
                ReciprocaLinkVerified = reciprocalVerified
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error verifying GitHub user");
            return new ProviderVerificationResult
            {
                Success = false,
                Error = "Failed to verify GitHub user"
            };
        }
    }

    /// <summary>
    /// Check if GitHub profile links back to user's website
    /// </summary>
    private bool CheckReciprocalLink(GitHubUser user, string userWebsiteUrl)
    {
        var normalizedUserUrl = NormalizeUrl(userWebsiteUrl);

        // Check blog/website field
        if (!string.IsNullOrEmpty(user.Blog))
        {
            var normalizedBlog = NormalizeUrl(user.Blog);
            if (normalizedBlog == normalizedUserUrl)
            {
                _logger.LogDebug("Reciprocal link found in blog field");
                return true;
            }
        }

        // Check bio for URL mention
        if (!string.IsNullOrEmpty(user.Bio))
        {
            // Check for exact URL
            if (user.Bio.Contains(userWebsiteUrl, StringComparison.OrdinalIgnoreCase))
                return true;
            
            // Check without protocol
            var hostAndPath = new Uri(userWebsiteUrl).Host + 
                new Uri(userWebsiteUrl).PathAndQuery.TrimEnd('/');
            if (user.Bio.Contains(hostAndPath, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        _logger.LogDebug(
            "No reciprocal link found. Blog: {Blog}, Bio: {Bio}, Expected: {Expected}",
            user.Blog, user.Bio, userWebsiteUrl);

        return false;
    }

    private static string NormalizeUrl(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return "";

        // Add https if no protocol
        if (!url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) && 
            !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            url = "https://" + url;
        }

        if (Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            // Return host + path, lowercase, no trailing slash
            return $"{uri.Host}{uri.AbsolutePath}".TrimEnd('/').ToLowerInvariant();
        }

        return url.TrimEnd('/').ToLowerInvariant();
    }
}
```

### GitHub API Models

```csharp
public class GitHubTokenResponse
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

public class GitHubUser
{
    [JsonPropertyName("id")]
    public long Id { get; set; }
    
    [JsonPropertyName("login")]
    public string Login { get; set; } = "";
    
    [JsonPropertyName("name")]
    public string? Name { get; set; }
    
    [JsonPropertyName("avatar_url")]
    public string? AvatarUrl { get; set; }
    
    [JsonPropertyName("html_url")]
    public string HtmlUrl { get; set; } = "";
    
    [JsonPropertyName("blog")]
    public string? Blog { get; set; }
    
    [JsonPropertyName("bio")]
    public string? Bio { get; set; }
    
    [JsonPropertyName("email")]
    public string? Email { get; set; }
}
```

### Provider Result Models

```csharp
public class ProviderMatch
{
    public string Username { get; set; } = "";
}

public class ProviderTokenResult
{
    public bool Success { get; set; }
    public string? AccessToken { get; set; }
    public string? Error { get; set; }
}

public class ProviderVerificationResult
{
    public bool Success { get; set; }
    public string? Username { get; set; }
    public string? ProfileUrl { get; set; }
    public string? DisplayName { get; set; }
    public string? AvatarUrl { get; set; }
    public string? Error { get; set; }
    public bool ReciprocaLinkVerified { get; set; }
}
```

## Adding New Identity Providers

To add a new provider (e.g., Twitter):

1. **Create provider class**:
```csharp
public class TwitterIdentityProvider : IIdentityProvider
{
    public string ProviderType => "twitter";
    public string DisplayName => "Twitter";
    // ... implement interface
}
```

2. **Add configuration**:
```json
{
  "Twitter": {
    "ClientId": "...",
    "ClientSecret": "..."
  }
}
```

3. **Register in DI**:
```csharp
builder.Services.AddSingleton<IIdentityProvider, TwitterIdentityProvider>();
```

4. **Add callback route**:
```csharp
[HttpGet("twitter")]
public Task<IActionResult> TwitterCallback([FromQuery] string code, [FromQuery] string state)
    => HandleProviderCallback("twitter", code, state);
```

## Provider State Management

Provider OAuth uses a separate state from the client's state to prevent confusion:

```csharp
public class ProviderState
{
    [Key]
    public string State { get; set; } = "";  // Random token
    public string PendingAuthenticationId { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public DateTime ExpiresAt { get; set; }
}

// Generate state
public async Task<string> GenerateProviderStateAsync(string pendingAuthId)
{
    var state = SecureTokenGenerator.GenerateToken(24);
    
    _dbContext.ProviderStates.Add(new ProviderState
    {
        State = state,
        PendingAuthenticationId = pendingAuthId,
        CreatedAt = DateTime.UtcNow,
        ExpiresAt = DateTime.UtcNow.AddMinutes(10)
    });
    
    await _dbContext.SaveChangesAsync();
    return state;
}

// Validate state
public async Task<string?> ValidateProviderStateAsync(string state)
{
    var providerState = await _dbContext.ProviderStates
        .FirstOrDefaultAsync(s => s.State == state);
    
    if (providerState == null || providerState.ExpiresAt < DateTime.UtcNow)
        return null;
    
    // Delete used state
    _dbContext.ProviderStates.Remove(providerState);
    await _dbContext.SaveChangesAsync();
    
    return providerState.PendingAuthenticationId;
}
```

## Error Handling

### Provider Errors

| Error | Description | User Action |
|-------|-------------|-------------|
| `token_exchange_failed` | Failed to get token from provider | Retry sign-in |
| `verification_failed` | User doesn't match expected profile | Check rel="me" link |
| `user_denied` | User cancelled OAuth | Try again |
| `provider_unavailable` | Provider API down | Try later |

### Error Page

```vue
<template>
  <div class="min-h-screen bg-gray-100 flex items-center justify-center p-4">
    <div class="bg-white rounded-lg shadow-lg max-w-md w-full p-6 text-center">
      <div class="w-16 h-16 bg-red-100 rounded-full flex items-center justify-center mx-auto mb-4">
        <svg class="w-8 h-8 text-red-600" fill="none" stroke="currentColor" viewBox="0 0 24 24">
          <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" 
                d="M6 18L18 6M6 6l12 12"/>
        </svg>
      </div>
      
      <h1 class="text-xl font-semibold text-gray-900 mb-2">Authentication Failed</h1>
      <p class="text-gray-600 mb-6">{{ errorMessage }}</p>
      
      <div v-if="showHelp" class="bg-gray-50 rounded-lg p-4 text-left mb-6">
        <p class="text-sm font-medium text-gray-700 mb-2">To fix this:</p>
        <ol class="text-sm text-gray-600 space-y-1 list-decimal list-inside">
          <li>Add a <code class="bg-gray-200 px-1">rel="me"</code> link to your site</li>
          <li>Add your website URL to your GitHub profile</li>
          <li>Try signing in again</li>
        </ol>
      </div>
      
      <button 
        @click="retry"
        class="px-6 py-2 bg-indigo-600 text-white rounded-lg hover:bg-indigo-700"
      >
        Try Again
      </button>
    </div>
  </div>
</template>
```

## Security Considerations

1. **State Parameter**: Use cryptographic random state for OAuth
2. **Token Handling**: Never log access tokens
3. **Minimal Scopes**: Only request `read:user` from GitHub
4. **Timeout**: Pending authentications expire after 30 minutes
5. **Provider Tokens**: Not stored - only used for verification

## Testing

```bash
# Test GitHub profile matching
echo "Testing github.com/janedoe"
curl -s "http://localhost:5000/api/test/match-provider?url=https://github.com/janedoe"

# Test full flow (manual)
# 1. Start authorization
open "http://localhost:5000/auth?response_type=code&client_id=https://test.example.com/&redirect_uri=https://test.example.com/callback&state=test123&code_challenge=E9Melhoa2OwvFrEMTJguCHaoeK1t8URWbuGJSstw-cM&code_challenge_method=S256&me=https://your-site.com/"
```

## Next Steps

After implementing authentication:
1. Update database schema → [07-database.md](./07-database.md)
2. Implement token endpoint → [04-token-endpoint.md](./04-token-endpoint.md)
