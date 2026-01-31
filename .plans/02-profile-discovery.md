# 02 - Profile Discovery (RelMeAuth)

## Implementation Status: ✅ COMPLETED (January 31, 2026)

### What Was Implemented
- ProfileDiscoveryService with full rel="me" link discovery from user websites
- HTML parsing using HtmlAgilityPack to extract rel="me" links from both `<a>` and `<link>` elements
- Relative URL resolution to absolute URLs
- Discovery of IndieAuth endpoints (authorization_endpoint, token_endpoint) from both HTTP Link headers and HTML `<link>` elements
- URL normalization (adding scheme if missing, trailing slash for domain-only URLs)
- GitHubIdentityProvider with URL pattern matching to identify GitHub profile URLs
- Reciprocal link verification (checking GitHub profile blog/bio for user's website URL)
- Integration with IdentityProviderFactory for extensible provider matching
- HTTP client configuration with timeout and user agent

---

## Overview

Profile Discovery is the process of fetching a user's personal website and discovering supported identity providers through `rel="me"` links. This is the foundation of how Talos authenticates users without storing credentials.

## How RelMeAuth Works

[RelMeAuth](https://microformats.org/wiki/RelMeAuth) is a standard for using `rel="me"` links for authentication:

1. User provides their website URL (e.g., `https://jane.example.com/`)
2. Talos fetches the URL and parses HTML for `rel="me"` links
3. Links point to user profiles on identity providers (e.g., GitHub)
4. User authenticates with the identity provider
5. Talos verifies the identity provider profile links back to the user's website (reciprocal verification)

## Discovery Flow

```
┌──────────────┐     ┌──────────────┐     ┌──────────────┐
│    Talos     │     │   User's     │     │   GitHub     │
│   Server     │     │   Website    │     │   Profile    │
└──────┬───────┘     └──────┬───────┘     └──────┬───────┘
       │                    │                    │
       │ 1. GET https://jane.example.com/       │
       │───────────────────►│                    │
       │                    │                    │
       │ 2. HTML with rel="me" links            │
       │◄───────────────────│                    │
       │                    │                    │
       │ 3. Parse HTML, extract rel="me" links  │
       │ Found: github.com/janedoe              │
       │                    │                    │
       │ 4. Match against supported providers   │
       │ Result: GitHub (janedoe)               │
       │                    │                    │
       │       ... User authenticates with GitHub ...
       │                    │                    │
       │ 5. GET github.com/janedoe (profile)    │
       │────────────────────┼───────────────────►│
       │                    │                    │
       │ 6. Check for reciprocal link           │
       │    Does GitHub profile link to         │
       │    jane.example.com?                   │
       │◄───────────────────┼────────────────────│
       │                    │                    │
       │ 7. If match: User verified ✓           │
       │                    │                    │
```

## User's Website Requirements

For a user's website to work with Talos, it needs:

### 1. Link Tags for Talos Endpoints

```html
<head>
    <link rel="authorization_endpoint" href="https://talos.example.com/auth">
    <link rel="token_endpoint" href="https://talos.example.com/token">
</head>
```

### 2. rel="me" Links to Identity Providers

```html
<!-- In <head> -->
<link rel="me" href="https://github.com/janedoe">

<!-- Or in <body> as visible links -->
<a href="https://github.com/janedoe" rel="me">GitHub</a>
<a href="https://twitter.com/janedoe" rel="me">Twitter</a>
```

### 3. Reciprocal Link on Identity Provider

On GitHub profile, the user must add their website URL to the "Website" or "Bio" field.

## Implementation

### Profile Discovery Service

```csharp
public interface IProfileDiscoveryService
{
    Task<ProfileDiscoveryResult> DiscoverAsync(string profileUrl);
}

public class ProfileDiscoveryResult
{
    public bool Success { get; set; }
    public string? Error { get; set; }
    public string ProfileUrl { get; set; } = "";
    public List<DiscoveredProvider> Providers { get; set; } = new();
}

public class DiscoveredProvider
{
    public string ProviderType { get; set; } = "";  // "github", "twitter", etc.
    public string ProfileUrl { get; set; } = "";     // Full URL to profile
    public string Username { get; set; } = "";       // Extracted username
    public string DisplayName { get; set; } = "";    // Human-readable name
    public string? IconUrl { get; set; }             // Provider icon
}
```

### Implementation

```csharp
public class ProfileDiscoveryService : IProfileDiscoveryService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IEnumerable<IIdentityProvider> _providers;
    private readonly ILogger<ProfileDiscoveryService> _logger;

    public ProfileDiscoveryService(
        IHttpClientFactory httpClientFactory,
        IEnumerable<IIdentityProvider> providers,
        ILogger<ProfileDiscoveryService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _providers = providers;
        _logger = logger;
    }

    public async Task<ProfileDiscoveryResult> DiscoverAsync(string profileUrl)
    {
        var result = new ProfileDiscoveryResult { ProfileUrl = profileUrl };

        try
        {
            // Validate URL
            if (!IsValidProfileUrl(profileUrl))
            {
                result.Error = "Invalid profile URL";
                return result;
            }

            // Fetch the profile page
            var client = _httpClientFactory.CreateClient("ProfileDiscovery");
            client.Timeout = TimeSpan.FromSeconds(10);
            
            var response = await client.GetAsync(profileUrl);
            if (!response.IsSuccessStatusCode)
            {
                result.Error = $"Failed to fetch profile: HTTP {response.StatusCode}";
                return result;
            }

            var html = await response.Content.ReadAsStringAsync();

            // Parse HTML for rel="me" links
            var relMeLinks = ExtractRelMeLinks(html, profileUrl);
            
            if (relMeLinks.Count == 0)
            {
                result.Error = "No rel=\"me\" links found on profile";
                return result;
            }

            // Match links against supported providers
            foreach (var link in relMeLinks)
            {
                foreach (var provider in _providers)
                {
                    var match = provider.MatchProfileUrl(link);
                    if (match != null)
                    {
                        result.Providers.Add(new DiscoveredProvider
                        {
                            ProviderType = provider.ProviderType,
                            ProfileUrl = link,
                            Username = match.Username,
                            DisplayName = provider.DisplayName,
                            IconUrl = provider.IconUrl
                        });
                        break; // Only match each link to one provider
                    }
                }
            }

            if (result.Providers.Count == 0)
            {
                result.Error = "No supported identity providers found. " +
                    "Please add a rel=\"me\" link to your GitHub profile.";
                return result;
            }

            result.Success = true;
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error discovering profile: {ProfileUrl}", profileUrl);
            result.Error = "Failed to discover identity providers";
            return result;
        }
    }

    private bool IsValidProfileUrl(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            return false;

        // Must be HTTPS (except localhost for development)
        if (uri.Scheme != "https" && uri.Host != "localhost")
            return false;

        // No fragments
        if (!string.IsNullOrEmpty(uri.Fragment))
            return false;

        return true;
    }

    private List<string> ExtractRelMeLinks(string html, string baseUrl)
    {
        var links = new List<string>();
        var doc = new HtmlDocument();
        doc.LoadHtml(html);

        // Find all elements with rel="me"
        var relMeNodes = doc.DocumentNode.SelectNodes(
            "//*[@rel='me' and @href]");

        if (relMeNodes == null)
            return links;

        foreach (var node in relMeNodes)
        {
            var href = node.GetAttributeValue("href", null);
            if (string.IsNullOrWhiteSpace(href))
                continue;

            // Resolve relative URLs
            if (Uri.TryCreate(new Uri(baseUrl), href, out var absoluteUri))
            {
                links.Add(absoluteUri.ToString());
            }
        }

        return links.Distinct().ToList();
    }
}
```

### Identity Provider Interface

```csharp
public interface IIdentityProvider
{
    /// <summary>
    /// Unique identifier for this provider (e.g., "github")
    /// </summary>
    string ProviderType { get; }
    
    /// <summary>
    /// Human-readable name (e.g., "GitHub")
    /// </summary>
    string DisplayName { get; }
    
    /// <summary>
    /// URL to provider icon/logo
    /// </summary>
    string? IconUrl { get; }

    /// <summary>
    /// Check if a URL matches this provider's profile URL pattern
    /// </summary>
    ProviderMatch? MatchProfileUrl(string url);

    /// <summary>
    /// Generate OAuth authorization URL
    /// </summary>
    string GetAuthorizationUrl(string state, string redirectUri);

    /// <summary>
    /// Exchange authorization code for access token
    /// </summary>
    Task<ProviderTokenResult> ExchangeCodeAsync(string code, string redirectUri);

    /// <summary>
    /// Verify the authenticated user matches the expected profile
    /// </summary>
    Task<ProviderVerificationResult> VerifyAsync(
        string accessToken, 
        string expectedUsername,
        string userWebsiteUrl);
}

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
    public string? Error { get; set; }
    public bool ReciprocaLinkVerified { get; set; }
}
```

### GitHub Identity Provider

```csharp
public class GitHubIdentityProvider : IIdentityProvider
{
    private readonly GitHubSettings _settings;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<GitHubIdentityProvider> _logger;

    public string ProviderType => "github";
    public string DisplayName => "GitHub";
    public string? IconUrl => "https://github.githubassets.com/favicons/favicon.svg";

    public GitHubIdentityProvider(
        IOptions<GitHubSettings> settings,
        IHttpClientFactory httpClientFactory,
        ILogger<GitHubIdentityProvider> logger)
    {
        _settings = settings.Value;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public ProviderMatch? MatchProfileUrl(string url)
    {
        // Match patterns:
        // https://github.com/username
        // https://www.github.com/username
        
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            return null;

        if (uri.Host != "github.com" && uri.Host != "www.github.com")
            return null;

        var path = uri.AbsolutePath.Trim('/');
        
        // Must be just username, not a repo or other path
        if (string.IsNullOrEmpty(path) || path.Contains('/'))
            return null;

        // Exclude reserved paths
        var reserved = new[] { "login", "logout", "settings", "organizations", 
            "explore", "trending", "collections", "events", "sponsors",
            "features", "security", "team", "enterprise", "pricing" };
        
        if (reserved.Contains(path.ToLowerInvariant()))
            return null;

        return new ProviderMatch { Username = path };
    }

    public string GetAuthorizationUrl(string state, string redirectUri)
    {
        var queryParams = new Dictionary<string, string>
        {
            ["client_id"] = _settings.ClientId,
            ["redirect_uri"] = redirectUri,
            ["state"] = state,
            ["scope"] = "read:user"
        };

        var query = string.Join("&", queryParams
            .Select(kvp => $"{kvp.Key}={Uri.EscapeDataString(kvp.Value)}"));

        return $"{_settings.AuthorizationEndpoint}?{query}";
    }

    public async Task<ProviderTokenResult> ExchangeCodeAsync(
        string code, string redirectUri)
    {
        var client = _httpClientFactory.CreateClient();
        
        var request = new HttpRequestMessage(HttpMethod.Post, _settings.TokenEndpoint);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        
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
            _logger.LogWarning("GitHub token exchange failed: {Response}", content);
            return new ProviderTokenResult 
            { 
                Success = false, 
                Error = "Failed to exchange authorization code" 
            };
        }

        var tokenResponse = JsonSerializer.Deserialize<GitHubTokenResponse>(content);
        
        if (!string.IsNullOrEmpty(tokenResponse?.Error))
        {
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

    public async Task<ProviderVerificationResult> VerifyAsync(
        string accessToken, 
        string expectedUsername,
        string userWebsiteUrl)
    {
        var client = _httpClientFactory.CreateClient("GitHub");
        client.DefaultRequestHeaders.Authorization = 
            new AuthenticationHeaderValue("Bearer", accessToken);

        var response = await client.GetAsync("user");
        if (!response.IsSuccessStatusCode)
        {
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

        // Verify username matches
        if (!string.Equals(user.Login, expectedUsername, StringComparison.OrdinalIgnoreCase))
        {
            return new ProviderVerificationResult
            {
                Success = false,
                Error = $"GitHub username mismatch. Expected: {expectedUsername}, Got: {user.Login}"
            };
        }

        // Check for reciprocal link
        var reciprocalVerified = CheckReciprocalLink(user, userWebsiteUrl);

        return new ProviderVerificationResult
        {
            Success = true,
            Username = user.Login,
            ProfileUrl = user.HtmlUrl,
            ReciprocaLinkVerified = reciprocalVerified
        };
    }

    private bool CheckReciprocalLink(GitHubUser user, string userWebsiteUrl)
    {
        // Normalize the user's website URL for comparison
        var normalizedUserUrl = NormalizeUrl(userWebsiteUrl);

        // Check blog/website field
        if (!string.IsNullOrEmpty(user.Blog))
        {
            if (NormalizeUrl(user.Blog) == normalizedUserUrl)
                return true;
        }

        // Check bio for URL
        if (!string.IsNullOrEmpty(user.Bio))
        {
            if (user.Bio.Contains(userWebsiteUrl, StringComparison.OrdinalIgnoreCase))
                return true;
                
            // Also check without protocol
            var urlWithoutProtocol = new Uri(userWebsiteUrl).Host + 
                new Uri(userWebsiteUrl).PathAndQuery.TrimEnd('/');
            if (user.Bio.Contains(urlWithoutProtocol, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    private string NormalizeUrl(string url)
    {
        if (string.IsNullOrEmpty(url))
            return "";

        // Add https if no protocol
        if (!url.StartsWith("http://") && !url.StartsWith("https://"))
            url = "https://" + url;

        if (Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return $"{uri.Host}{uri.PathAndQuery}".TrimEnd('/').ToLowerInvariant();
        }

        return url.ToLowerInvariant();
    }
}

// GitHub API response models
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
    [JsonPropertyName("login")]
    public string Login { get; set; } = "";
    
    [JsonPropertyName("html_url")]
    public string HtmlUrl { get; set; } = "";
    
    [JsonPropertyName("blog")]
    public string? Blog { get; set; }
    
    [JsonPropertyName("bio")]
    public string? Bio { get; set; }
    
    [JsonPropertyName("name")]
    public string? Name { get; set; }
    
    [JsonPropertyName("avatar_url")]
    public string? AvatarUrl { get; set; }
}
```

## Error Handling

### No Providers Found

When no supported providers are discovered:

```json
{
  "success": false,
  "error": "No supported identity providers found",
  "message": "Your website must include rel=\"me\" links to supported providers.",
  "supportedProviders": [
    {
      "name": "GitHub",
      "example": "<a href=\"https://github.com/username\" rel=\"me\">GitHub</a>"
    }
  ]
}
```

### Reciprocal Link Not Found

When the identity provider profile doesn't link back:

```json
{
  "success": false,
  "error": "Reciprocal link verification failed",
  "message": "Your GitHub profile must include a link to your website.",
  "details": {
    "yourWebsite": "https://jane.example.com/",
    "githubProfile": "https://github.com/janedoe",
    "hint": "Add your website URL to your GitHub profile's 'Website' field."
  }
}
```

## Vue.js Components

### ProviderSelectView.vue

```vue
<template>
  <div class="min-h-screen bg-gray-100 flex items-center justify-center p-4">
    <div class="bg-white rounded-lg shadow-lg max-w-md w-full p-6">
      <!-- Loading -->
      <div v-if="loading" class="text-center py-8">
        <div class="animate-spin rounded-full h-8 w-8 border-b-2 border-indigo-600 mx-auto"></div>
        <p class="mt-4 text-gray-600">Discovering identity providers...</p>
      </div>

      <!-- Error -->
      <div v-else-if="error" class="text-center py-8">
        <div class="text-red-500 mb-4">
          <svg class="w-12 h-12 mx-auto" fill="none" stroke="currentColor" viewBox="0 0 24 24">
            <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" 
                  d="M12 9v2m0 4h.01m-6.938 4h13.856c1.54 0 2.502-1.667 1.732-3L13.732 4c-.77-1.333-2.694-1.333-3.464 0L3.34 16c-.77 1.333.192 3 1.732 3z"/>
          </svg>
        </div>
        <h2 class="text-lg font-semibold text-gray-900 mb-2">Unable to Sign In</h2>
        <p class="text-gray-600 mb-4">{{ error }}</p>
        
        <div v-if="supportedProviders.length > 0" class="text-left bg-gray-50 p-4 rounded-lg mt-4">
          <p class="text-sm font-medium text-gray-700 mb-2">Supported providers:</p>
          <ul class="text-sm text-gray-600 space-y-1">
            <li v-for="provider in supportedProviders" :key="provider.name">
              • {{ provider.name }}
            </li>
          </ul>
        </div>
      </div>

      <!-- Provider Selection -->
      <div v-else>
        <h1 class="text-xl font-semibold text-gray-900 mb-2">Sign In</h1>
        <p class="text-gray-600 mb-6">
          Choose how to verify your identity as 
          <strong>{{ profileUrl }}</strong>
        </p>

        <div class="space-y-3">
          <button
            v-for="provider in providers"
            :key="provider.providerType"
            @click="selectProvider(provider)"
            class="w-full flex items-center gap-3 p-4 border rounded-lg hover:bg-gray-50 transition-colors"
          >
            <img 
              v-if="provider.iconUrl" 
              :src="provider.iconUrl" 
              :alt="provider.displayName"
              class="w-6 h-6"
            >
            <div class="text-left">
              <p class="font-medium text-gray-900">{{ provider.displayName }}</p>
              <p class="text-sm text-gray-500">{{ provider.username }}</p>
            </div>
          </button>
        </div>
      </div>
    </div>
  </div>
</template>
```

## Security Considerations

1. **Timeout on Fetches**: Limit fetch time to prevent DoS
2. **URL Validation**: Strict validation of profile URLs
3. **HTML Parsing**: Safe parsing, no script execution
4. **Rate Limiting**: Limit discovery requests per IP
5. **SSRF Prevention**: Block private/internal IPs when fetching

## Testing

```bash
# Test profile discovery
curl "http://localhost:5000/api/discover?url=https://jane.example.com/"

# Expected response (success)
{
  "success": true,
  "profileUrl": "https://jane.example.com/",
  "providers": [
    {
      "providerType": "github",
      "profileUrl": "https://github.com/janedoe",
      "username": "janedoe",
      "displayName": "GitHub"
    }
  ]
}
```

## Next Steps

After implementing profile discovery:
1. Implement GitHub OAuth flow → [05-authentication.md](./05-authentication.md)
2. Update authorization endpoint → [03-authorization-endpoint.md](./03-authorization-endpoint.md)

