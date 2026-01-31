# 06 - Security

## Implementation Status: ✅ COMPLETED (January 31, 2026)

### What Was Implemented
- PkceService with S256 code challenge verification
- Base64URL encoding for PKCE challenges
- Code verifier format validation (43-128 chars, unreserved characters)
- Rate limiting using ASP.NET Core built-in RateLimiter
  - Global limit: 100 requests/minute per IP
  - Auth endpoint: 30 requests/minute per IP  
  - Token endpoint: 20 requests/minute per IP
- Security headers middleware:
  - X-Frame-Options: DENY
  - X-Content-Type-Options: nosniff
  - X-XSS-Protection: 1; mode=block
  - Referrer-Policy: strict-origin-when-cross-origin
  - Content-Security-Policy with strict defaults
- HTTPS redirection in production
- UrlValidator utility class for IndieAuth-compliant URL validation
  - Profile URL normalization
  - Client ID validation
  - Redirect URI validation against client ID

---

## Overview

This document covers the security features required for a compliant IndieAuth server, including PKCE, CSRF protection, rate limiting, and secure token handling.

## PKCE (Proof Key for Code Exchange)

PKCE prevents authorization code interception attacks. It's **required** by the IndieAuth specification.

### How PKCE Works

```
1. Client generates code_verifier (random string)
2. Client creates code_challenge = BASE64URL(SHA256(code_verifier))
3. Client sends code_challenge with authorization request
4. Server stores code_challenge with authorization code
5. Client sends code_verifier with token exchange
6. Server verifies: BASE64URL(SHA256(code_verifier)) == stored code_challenge
```

### PKCE Service Implementation

```csharp
public interface IPkceService
{
    bool VerifyCodeChallenge(string codeVerifier, string codeChallenge, string method);
    string GenerateCodeVerifier();
    string GenerateCodeChallenge(string codeVerifier);
}

public class PkceService : IPkceService
{
    /// <summary>
    /// Verifies the code_verifier against the stored code_challenge
    /// </summary>
    public bool VerifyCodeChallenge(string codeVerifier, string codeChallenge, string method)
    {
        if (string.IsNullOrEmpty(codeVerifier) || string.IsNullOrEmpty(codeChallenge))
            return false;

        if (method != "S256")
            return false;  // Only S256 is supported

        // Compute SHA256 hash of verifier
        using var sha256 = SHA256.Create();
        var challengeBytes = sha256.ComputeHash(Encoding.ASCII.GetBytes(codeVerifier));
        
        // Base64URL encode (no padding)
        var computedChallenge = Convert.ToBase64String(challengeBytes)
            .Replace("+", "-")
            .Replace("/", "_")
            .TrimEnd('=');

        // Constant-time comparison
        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(computedChallenge),
            Encoding.UTF8.GetBytes(codeChallenge));
    }

    /// <summary>
    /// Generates a cryptographically random code verifier
    /// </summary>
    public string GenerateCodeVerifier()
    {
        // 32 bytes = 43 characters in base64url
        var bytes = new byte[32];
        using var rng = RandomNumberGenerator.Create();
        rng.GetBytes(bytes);
        
        return Convert.ToBase64String(bytes)
            .Replace("+", "-")
            .Replace("/", "_")
            .TrimEnd('=');
    }

    /// <summary>
    /// Generates a code challenge from a code verifier
    /// </summary>
    public string GenerateCodeChallenge(string codeVerifier)
    {
        using var sha256 = SHA256.Create();
        var challengeBytes = sha256.ComputeHash(Encoding.ASCII.GetBytes(codeVerifier));
        
        return Convert.ToBase64String(challengeBytes)
            .Replace("+", "-")
            .Replace("/", "_")
            .TrimEnd('=');
    }
}
```

### PKCE Validation in Token Endpoint

```csharp
// In TokenController.HandleAuthorizationCodeGrant()

// Verify PKCE
if (!_pkceService.VerifyCodeChallenge(
    request.CodeVerifier,
    authCode.CodeChallenge,
    authCode.CodeChallengeMethod))
{
    _logger.LogWarning("PKCE verification failed for code {Code}", request.Code);
    return InvalidGrant("Invalid code_verifier");
}
```

## CSRF Protection

### State Parameter

The `state` parameter protects against CSRF attacks in the authorization flow.

```csharp
// In AuthController.Authorize()

// Validate state is present
if (string.IsNullOrEmpty(state))
{
    return BadRequest(new { error = "missing_state", error_description = "state parameter is required" });
}

// State should be at least 8 characters (entropy check)
if (state.Length < 8)
{
    return BadRequest(new { error = "invalid_state", error_description = "state must be at least 8 characters" });
}
```

### Cookie Protection

```csharp
// In Program.cs

builder.Services.AddAntiforgery(options =>
{
    options.Cookie.Name = "TalosAntiforgery";
    options.Cookie.HttpOnly = true;
    options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
    options.Cookie.SameSite = SameSiteMode.Strict;
});
```

### Vue.js CSRF Token Handling

```typescript
// api/axios.ts
import axios from 'axios'

const api = axios.create({
  baseURL: '/api',
  withCredentials: true,
  xsrfCookieName: 'XSRF-TOKEN',
  xsrfHeaderName: 'X-XSRF-TOKEN'
})

export default api
```

## Rate Limiting

Protect against brute force and DoS attacks.

### Rate Limiting Middleware

```csharp
// Install package: dotnet add package AspNetCoreRateLimit

// In Program.cs
builder.Services.AddMemoryCache();
builder.Services.Configure<IpRateLimitOptions>(options =>
{
    options.EnableEndpointRateLimiting = true;
    options.StackBlockedRequests = false;
    options.RealIpHeader = "X-Forwarded-For";
    options.ClientIdHeader = "X-ClientId";
    options.HttpStatusCode = 429;
    
    options.GeneralRules = new List<RateLimitRule>
    {
        // Global limit
        new RateLimitRule
        {
            Endpoint = "*",
            Period = "1m",
            Limit = 100
        }
    };
    
    options.EndpointRules = new List<RateLimitRule>
    {
        // Login endpoint - stricter limits
        new RateLimitRule
        {
            Endpoint = "POST:/api/account/login",
            Period = "1m",
            Limit = 5
        },
        new RateLimitRule
        {
            Endpoint = "POST:/api/account/login",
            Period = "1h",
            Limit = 20
        },
        
        // Token endpoint
        new RateLimitRule
        {
            Endpoint = "POST:/token",
            Period = "1m",
            Limit = 30
        },
        
        // Authorization endpoint
        new RateLimitRule
        {
            Endpoint = "GET:/auth",
            Period = "1m",
            Limit = 30
        }
    };
});

builder.Services.AddSingleton<IRateLimitConfiguration, RateLimitConfiguration>();
builder.Services.AddInMemoryRateLimiting();

// In middleware
app.UseIpRateLimiting();
```

### Custom Rate Limiter (Alternative)

```csharp
public class LoginRateLimiter
{
    private readonly IMemoryCache _cache;
    private readonly ILogger<LoginRateLimiter> _logger;

    public LoginRateLimiter(IMemoryCache cache, ILogger<LoginRateLimiter> logger)
    {
        _cache = cache;
        _logger = logger;
    }

    public bool IsRateLimited(string ipAddress)
    {
        var key = $"login_attempts_{ipAddress}";
        var attempts = _cache.GetOrCreate(key, entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(15);
            return 0;
        });

        return attempts >= 5;
    }

    public void RecordFailedAttempt(string ipAddress)
    {
        var key = $"login_attempts_{ipAddress}";
        var attempts = _cache.GetOrCreate(key, entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(15);
            return 0;
        });

        _cache.Set(key, attempts + 1, TimeSpan.FromMinutes(15));
        
        if (attempts >= 3)
        {
            _logger.LogWarning("Multiple failed login attempts from {IP}", ipAddress);
        }
    }

    public void ClearAttempts(string ipAddress)
    {
        var key = $"login_attempts_{ipAddress}";
        _cache.Remove(key);
    }
}
```

## HTTPS Enforcement

IndieAuth **requires** HTTPS for all endpoints.

```csharp
// In Program.cs

if (!app.Environment.IsDevelopment())
{
    app.UseHttpsRedirection();
    app.UseHsts();
}

// Force HTTPS via middleware
app.Use(async (context, next) =>
{
    if (!context.Request.IsHttps && !context.Request.Host.Host.Equals("localhost"))
    {
        var httpsUrl = $"https://{context.Request.Host}{context.Request.Path}{context.Request.QueryString}";
        context.Response.Redirect(httpsUrl, permanent: true);
        return;
    }
    await next();
});
```

### HSTS Configuration

```csharp
builder.Services.AddHsts(options =>
{
    options.Preload = true;
    options.IncludeSubDomains = true;
    options.MaxAge = TimeSpan.FromDays(365);
});
```

## Secure Token Storage

### Authorization Codes

```csharp
public class AuthorizationCode
{
    [Key]
    public string Code { get; set; } = "";  // Cryptographically random
    
    // Never store the code_verifier, only the challenge
    public string CodeChallenge { get; set; } = "";
    public string CodeChallengeMethod { get; set; } = "S256";
    
    // ... other fields
    
    public DateTime ExpiresAt { get; set; }  // Short-lived (10 min)
    public bool Used { get; set; } = false;  // Single use
}
```

### Generating Secure Codes

```csharp
public static class SecureTokenGenerator
{
    /// <summary>
    /// Generates a cryptographically secure random token
    /// </summary>
    public static string GenerateToken(int byteLength = 32)
    {
        var bytes = new byte[byteLength];
        using var rng = RandomNumberGenerator.Create();
        rng.GetBytes(bytes);
        
        // URL-safe base64
        return Convert.ToBase64String(bytes)
            .Replace("+", "-")
            .Replace("/", "_")
            .TrimEnd('=');
    }

    /// <summary>
    /// Generates a shorter authorization code (still secure)
    /// </summary>
    public static string GenerateAuthorizationCode()
    {
        return GenerateToken(24);  // 32 characters
    }

    /// <summary>
    /// Generates a refresh token
    /// </summary>
    public static string GenerateRefreshToken()
    {
        return GenerateToken(32);  // 43 characters
    }
}
```

## Security Headers

```csharp
// In Program.cs

app.Use(async (context, next) =>
{
    var headers = context.Response.Headers;
    
    // Prevent clickjacking
    headers["X-Frame-Options"] = "DENY";
    
    // Prevent MIME sniffing
    headers["X-Content-Type-Options"] = "nosniff";
    
    // XSS protection (legacy browsers)
    headers["X-XSS-Protection"] = "1; mode=block";
    
    // Referrer policy
    headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
    
    // Content Security Policy
    headers["Content-Security-Policy"] = 
        "default-src 'self'; " +
        "script-src 'self'; " +
        "style-src 'self' 'unsafe-inline'; " +
        "img-src 'self' https: data:; " +
        "font-src 'self'; " +
        "connect-src 'self'; " +
        "frame-ancestors 'none';";
    
    // Permissions Policy
    headers["Permissions-Policy"] = 
        "accelerometer=(), camera=(), geolocation=(), gyroscope=(), magnetometer=(), microphone=(), payment=(), usb=()";
    
    await next();
});
```

## Input Validation

### URL Validation

```csharp
public static class UrlValidator
{
    public static bool IsValidHttpsUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return false;

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            return false;

        // Must be HTTPS (except localhost for development)
        if (uri.Scheme != "https" && uri.Host != "localhost")
            return false;

        // No fragments allowed
        if (!string.IsNullOrEmpty(uri.Fragment))
            return false;

        // No userinfo allowed
        if (!string.IsNullOrEmpty(uri.UserInfo))
            return false;

        return true;
    }

    public static bool IsValidClientId(string? clientId)
    {
        if (!IsValidHttpsUrl(clientId))
            return false;

        var uri = new Uri(clientId!);
        
        // client_id must have a path (can be just "/")
        if (string.IsNullOrEmpty(uri.AbsolutePath))
            return false;

        return true;
    }

    public static bool RedirectUriMatchesClient(string clientId, string redirectUri)
    {
        var clientUri = new Uri(clientId);
        var redirectUriParsed = new Uri(redirectUri);

        // Must be same scheme
        if (clientUri.Scheme != redirectUriParsed.Scheme)
            return false;

        // Must be same host
        if (!clientUri.Host.Equals(redirectUriParsed.Host, StringComparison.OrdinalIgnoreCase))
            return false;

        // Port must match (or both be default)
        if (clientUri.Port != redirectUriParsed.Port)
            return false;

        return true;
    }
}
```

### Scope Validation

```csharp
public static class ScopeValidator
{
    private static readonly HashSet<string> AllowedScopes = new()
    {
        "profile", "email", "create", "update", "delete", "media"
    };

    public static string? ValidateAndNormalize(string? scope)
    {
        if (string.IsNullOrWhiteSpace(scope))
            return null;

        var requestedScopes = scope.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var validScopes = requestedScopes
            .Where(s => AllowedScopes.Contains(s.ToLowerInvariant()))
            .Distinct()
            .OrderBy(s => s);

        return string.Join(" ", validScopes);
    }

    public static bool ContainsScope(string? grantedScopes, string requiredScope)
    {
        if (string.IsNullOrWhiteSpace(grantedScopes))
            return false;

        return grantedScopes
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Contains(requiredScope, StringComparer.OrdinalIgnoreCase);
    }
}
```

## Logging and Monitoring

```csharp
// Security event logging
public class SecurityEventLogger
{
    private readonly ILogger<SecurityEventLogger> _logger;

    public void LogAuthorizationRequest(string clientId, string scope, string ipAddress)
    {
        _logger.LogInformation(
            "Authorization request from client {ClientId} for scope '{Scope}' from IP {IP}",
            clientId, scope, ipAddress);
    }

    public void LogAuthorizationGranted(string clientId, string userId)
    {
        _logger.LogInformation(
            "Authorization granted to client {ClientId} for user {UserId}",
            clientId, userId);
    }

    public void LogAuthorizationDenied(string clientId, string userId)
    {
        _logger.LogWarning(
            "Authorization denied to client {ClientId} by user {UserId}",
            clientId, userId);
    }

    public void LogTokenIssued(string clientId, string userId)
    {
        _logger.LogInformation(
            "Token issued to client {ClientId} for user {UserId}",
            clientId, userId);
    }

    public void LogTokenRevoked(string tokenPrefix, string reason)
    {
        _logger.LogInformation(
            "Token revoked: {TokenPrefix}... Reason: {Reason}",
            tokenPrefix, reason);
    }

    public void LogFailedLogin(string username, string ipAddress)
    {
        _logger.LogWarning(
            "Failed login attempt for user '{Username}' from IP {IP}",
            username, ipAddress);
    }

    public void LogSuspiciousActivity(string activity, string ipAddress)
    {
        _logger.LogWarning(
            "Suspicious activity detected: {Activity} from IP {IP}",
            activity, ipAddress);
    }
}
```

## Security Checklist

### Authorization Flow
- [ ] PKCE required (S256 only)
- [ ] State parameter required (min 8 chars)
- [ ] Authorization codes expire in 10 minutes
- [ ] Authorization codes are single-use
- [ ] client_id and redirect_uri validated
- [ ] redirect_uri matches client_id domain

### Token Handling
- [ ] JWT access tokens are short-lived (15 min)
- [ ] Refresh tokens are cryptographically random
- [ ] Refresh tokens can be revoked
- [ ] Constant-time token comparison
- [ ] Tokens never logged

### Authentication
- [ ] Passwords hashed with BCrypt (work factor 12+)
- [ ] Timing-safe password comparison
- [ ] Rate limiting on login endpoint
- [ ] Random delay on failed login

### Transport Security
- [ ] HTTPS required for all endpoints
- [ ] HSTS enabled
- [ ] Secure cookie attributes set
- [ ] Security headers configured

### Input Validation
- [ ] All URLs validated
- [ ] Scopes validated against allowed list
- [ ] Input length limits enforced

## Next Steps

After implementing security features:
1. Set up database → [07-database.md](./07-database.md)
2. Testing → [08-testing.md](./08-testing.md)

