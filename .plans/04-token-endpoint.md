# 04 - Token Endpoint

## Overview

The token endpoint (`/token`) exchanges authorization codes for access tokens and handles refresh token flows. Talos uses:
- **JWT access tokens** (short-lived, 15 minutes)
- **Opaque refresh tokens** (long-lived, stored in database, revokable)

## Token Flow

```
┌─────────────┐                              ┌─────────────┐
│   Client    │                              │   Talos     │
│    App      │                              │   /token    │
└──────┬──────┘                              └──────┬──────┘
       │                                            │
       │  1. POST code + code_verifier              │
       │ ─────────────────────────────────────────► │
       │                                            │
       │                    ┌───────────────────────┴───────────────────────┐
       │                    │  2. Validate authorization code               │
       │                    │  3. Verify PKCE (code_verifier vs challenge)  │
       │                    │  4. Verify client_id and redirect_uri match   │
       │                    │  5. Mark code as used                         │
       │                    │  6. Generate JWT access token                 │
       │                    │  7. Generate opaque refresh token             │
       │                    │  8. Store refresh token in database           │
       │                    └───────────────────────┬───────────────────────┘
       │                                            │
       │  9. Return tokens + profile                │
       │ ◄───────────────────────────────────────── │
       │                                            │
```

## Endpoints

### POST /token - Token Exchange

Exchanges authorization code for tokens.

#### Request (application/x-www-form-urlencoded)

| Parameter | Required | Description |
|-----------|----------|-------------|
| `grant_type` | Yes | `authorization_code` or `refresh_token` |
| `code` | For auth_code | The authorization code received |
| `redirect_uri` | For auth_code | Must match original request |
| `client_id` | Yes | Client application URL |
| `code_verifier` | For auth_code | PKCE code verifier |
| `refresh_token` | For refresh | The refresh token |

#### Response (application/json)

```json
{
  "access_token": "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9...",
  "token_type": "Bearer",
  "expires_in": 900,
  "refresh_token": "dGhpcyBpcyBhbiBvcGFxdWUgdG9rZW4...",
  "scope": "profile email",
  "me": "https://example.com/"
}
```

### POST /token/refresh - Refresh Token

Uses refresh token to get new access token.

### POST /token/revoke - Revoke Token

Revokes a refresh token.

### POST /token/introspect - Token Introspection

Returns information about a token.

## Implementation

### TokenController.cs

```csharp
[Route("token")]
[ApiController]
public class TokenController : ControllerBase
{
    private readonly ITokenService _tokenService;
    private readonly IAuthorizationService _authService;
    private readonly IPkceService _pkceService;
    private readonly UserSettings _userSettings;
    private readonly ILogger<TokenController> _logger;

    public TokenController(
        ITokenService tokenService,
        IAuthorizationService authService,
        IPkceService pkceService,
        IOptions<UserSettings> userSettings,
        ILogger<TokenController> logger)
    {
        _tokenService = tokenService;
        _authService = authService;
        _pkceService = pkceService;
        _userSettings = userSettings.Value;
        _logger = logger;
    }

    /// <summary>
    /// Token endpoint - exchange code for tokens
    /// </summary>
    [HttpPost]
    [Consumes("application/x-www-form-urlencoded")]
    public async Task<IActionResult> Token([FromForm] TokenRequest request)
    {
        return request.GrantType switch
        {
            "authorization_code" => await HandleAuthorizationCodeGrant(request),
            "refresh_token" => await HandleRefreshTokenGrant(request),
            _ => BadRequest(new TokenErrorResponse
            {
                Error = "unsupported_grant_type",
                ErrorDescription = "Grant type must be 'authorization_code' or 'refresh_token'"
            })
        };
    }

    private async Task<IActionResult> HandleAuthorizationCodeGrant(TokenRequest request)
    {
        // Validate required parameters
        if (string.IsNullOrEmpty(request.Code))
            return InvalidRequest("Missing 'code' parameter");
        
        if (string.IsNullOrEmpty(request.ClientId))
            return InvalidRequest("Missing 'client_id' parameter");
        
        if (string.IsNullOrEmpty(request.RedirectUri))
            return InvalidRequest("Missing 'redirect_uri' parameter");
        
        if (string.IsNullOrEmpty(request.CodeVerifier))
            return InvalidRequest("Missing 'code_verifier' parameter");

        // Retrieve and validate authorization code
        var authCode = await _authService.GetAuthorizationCodeAsync(request.Code);
        
        if (authCode == null)
            return InvalidGrant("Invalid authorization code");
        
        if (authCode.Used)
            return InvalidGrant("Authorization code already used");
        
        if (authCode.ExpiresAt < DateTime.UtcNow)
            return InvalidGrant("Authorization code expired");
        
        if (authCode.ClientId != request.ClientId)
            return InvalidGrant("client_id mismatch");
        
        if (authCode.RedirectUri != request.RedirectUri)
            return InvalidGrant("redirect_uri mismatch");

        // Verify PKCE
        if (!_pkceService.VerifyCodeChallenge(
            request.CodeVerifier, 
            authCode.CodeChallenge, 
            authCode.CodeChallengeMethod))
        {
            return InvalidGrant("Invalid code_verifier");
        }

        // Mark code as used
        await _authService.MarkCodeAsUsedAsync(request.Code);

        // Generate tokens
        var accessToken = _tokenService.GenerateAccessToken(
            _userSettings.ProfileUrl,
            authCode.Scope,
            authCode.ClientId
        );

        var refreshToken = await _tokenService.GenerateRefreshTokenAsync(
            _userSettings.ProfileUrl,
            authCode.Scope,
            authCode.ClientId
        );

        return Ok(new TokenResponse
        {
            AccessToken = accessToken,
            TokenType = "Bearer",
            ExpiresIn = 900, // 15 minutes
            RefreshToken = refreshToken,
            Scope = authCode.Scope,
            Me = _userSettings.ProfileUrl
        });
    }

    private async Task<IActionResult> HandleRefreshTokenGrant(TokenRequest request)
    {
        if (string.IsNullOrEmpty(request.RefreshToken))
            return InvalidRequest("Missing 'refresh_token' parameter");

        if (string.IsNullOrEmpty(request.ClientId))
            return InvalidRequest("Missing 'client_id' parameter");

        // Validate refresh token
        var storedToken = await _tokenService.GetRefreshTokenAsync(request.RefreshToken);
        
        if (storedToken == null)
            return InvalidGrant("Invalid refresh token");
        
        if (storedToken.Revoked)
            return InvalidGrant("Refresh token has been revoked");
        
        if (storedToken.ExpiresAt < DateTime.UtcNow)
            return InvalidGrant("Refresh token expired");
        
        if (storedToken.ClientId != request.ClientId)
            return InvalidGrant("client_id mismatch");

        // Generate new access token (keep same refresh token)
        var accessToken = _tokenService.GenerateAccessToken(
            storedToken.UserId,
            storedToken.Scope,
            storedToken.ClientId
        );

        return Ok(new TokenResponse
        {
            AccessToken = accessToken,
            TokenType = "Bearer",
            ExpiresIn = 900,
            RefreshToken = request.RefreshToken, // Return same refresh token
            Scope = storedToken.Scope,
            Me = storedToken.UserId
        });
    }

    /// <summary>
    /// Revoke a refresh token
    /// </summary>
    [HttpPost("revoke")]
    [Consumes("application/x-www-form-urlencoded")]
    public async Task<IActionResult> Revoke([FromForm] RevokeRequest request)
    {
        if (string.IsNullOrEmpty(request.Token))
            return InvalidRequest("Missing 'token' parameter");

        await _tokenService.RevokeRefreshTokenAsync(request.Token);
        
        // Always return 200 per RFC 7009
        return Ok();
    }

    /// <summary>
    /// Token introspection (RFC 7662)
    /// </summary>
    [HttpPost("introspect")]
    [Consumes("application/x-www-form-urlencoded")]
    public async Task<IActionResult> Introspect([FromForm] IntrospectRequest request)
    {
        if (string.IsNullOrEmpty(request.Token))
            return InvalidRequest("Missing 'token' parameter");

        // Try as access token (JWT)
        var accessTokenClaims = _tokenService.ValidateAccessToken(request.Token);
        if (accessTokenClaims != null)
        {
            return Ok(new IntrospectionResponse
            {
                Active = true,
                Me = accessTokenClaims.FindFirst("me")?.Value,
                ClientId = accessTokenClaims.FindFirst("client_id")?.Value,
                Scope = accessTokenClaims.FindFirst("scope")?.Value,
                Exp = long.Parse(accessTokenClaims.FindFirst("exp")?.Value ?? "0"),
                Iat = long.Parse(accessTokenClaims.FindFirst("iat")?.Value ?? "0")
            });
        }

        // Try as refresh token
        var refreshToken = await _tokenService.GetRefreshTokenAsync(request.Token);
        if (refreshToken != null && !refreshToken.Revoked && refreshToken.ExpiresAt > DateTime.UtcNow)
        {
            return Ok(new IntrospectionResponse
            {
                Active = true,
                Me = refreshToken.UserId,
                ClientId = refreshToken.ClientId,
                Scope = refreshToken.Scope,
                Exp = new DateTimeOffset(refreshToken.ExpiresAt).ToUnixTimeSeconds(),
                Iat = new DateTimeOffset(refreshToken.CreatedAt).ToUnixTimeSeconds()
            });
        }

        // Token is invalid or expired
        return Ok(new IntrospectionResponse { Active = false });
    }

    private IActionResult InvalidRequest(string description) =>
        BadRequest(new TokenErrorResponse
        {
            Error = "invalid_request",
            ErrorDescription = description
        });

    private IActionResult InvalidGrant(string description) =>
        BadRequest(new TokenErrorResponse
        {
            Error = "invalid_grant",
            ErrorDescription = description
        });
}
```

### Token Service

```csharp
public interface ITokenService
{
    string GenerateAccessToken(string userId, string? scope, string clientId);
    Task<string> GenerateRefreshTokenAsync(string userId, string? scope, string clientId);
    ClaimsPrincipal? ValidateAccessToken(string token);
    Task<RefreshToken?> GetRefreshTokenAsync(string token);
    Task RevokeRefreshTokenAsync(string token);
}

public class TokenService : ITokenService
{
    private readonly JwtSettings _jwtSettings;
    private readonly IndieAuthSettings _indieAuthSettings;
    private readonly TalosDbContext _dbContext;

    public TokenService(
        IOptions<JwtSettings> jwtSettings,
        IOptions<IndieAuthSettings> indieAuthSettings,
        TalosDbContext dbContext)
    {
        _jwtSettings = jwtSettings.Value;
        _indieAuthSettings = indieAuthSettings.Value;
        _dbContext = dbContext;
    }

    public string GenerateAccessToken(string userId, string? scope, string clientId)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_jwtSettings.SecretKey));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var claims = new List<Claim>
        {
            new Claim("me", userId),
            new Claim("client_id", clientId),
            new Claim(JwtRegisteredClaimNames.Iat, 
                DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(), 
                ClaimValueTypes.Integer64)
        };

        if (!string.IsNullOrEmpty(scope))
        {
            claims.Add(new Claim("scope", scope));
        }

        var token = new JwtSecurityToken(
            issuer: _jwtSettings.Issuer,
            audience: _jwtSettings.Audience,
            claims: claims,
            expires: DateTime.UtcNow.AddMinutes(_jwtSettings.AccessTokenExpirationMinutes),
            signingCredentials: credentials
        );

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    public async Task<string> GenerateRefreshTokenAsync(string userId, string? scope, string clientId)
    {
        // Generate cryptographically secure opaque token
        var tokenBytes = new byte[32];
        using var rng = RandomNumberGenerator.Create();
        rng.GetBytes(tokenBytes);
        var token = Convert.ToBase64String(tokenBytes)
            .Replace("+", "-")
            .Replace("/", "_")
            .TrimEnd('=');

        // Store in database
        var refreshToken = new RefreshToken
        {
            Token = token,
            UserId = userId,
            ClientId = clientId,
            Scope = scope,
            CreatedAt = DateTime.UtcNow,
            ExpiresAt = DateTime.UtcNow.AddDays(_indieAuthSettings.RefreshTokenExpirationDays),
            Revoked = false
        };

        _dbContext.RefreshTokens.Add(refreshToken);
        await _dbContext.SaveChangesAsync();

        return token;
    }

    public ClaimsPrincipal? ValidateAccessToken(string token)
    {
        try
        {
            var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_jwtSettings.SecretKey));
            var tokenHandler = new JwtSecurityTokenHandler();

            var principal = tokenHandler.ValidateToken(token, new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidIssuer = _jwtSettings.Issuer,
                ValidateAudience = true,
                ValidAudience = _jwtSettings.Audience,
                ValidateLifetime = true,
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = key,
                ClockSkew = TimeSpan.FromMinutes(1)
            }, out _);

            return principal;
        }
        catch
        {
            return null;
        }
    }

    public async Task<RefreshToken?> GetRefreshTokenAsync(string token)
    {
        return await _dbContext.RefreshTokens
            .FirstOrDefaultAsync(t => t.Token == token);
    }

    public async Task RevokeRefreshTokenAsync(string token)
    {
        var refreshToken = await _dbContext.RefreshTokens
            .FirstOrDefaultAsync(t => t.Token == token);
        
        if (refreshToken != null)
        {
            refreshToken.Revoked = true;
            refreshToken.RevokedAt = DateTime.UtcNow;
            await _dbContext.SaveChangesAsync();
        }
    }
}
```

### Models

```csharp
// Models/TokenRequest.cs
public class TokenRequest
{
    [FromForm(Name = "grant_type")]
    public string GrantType { get; set; } = "";
    
    [FromForm(Name = "code")]
    public string? Code { get; set; }
    
    [FromForm(Name = "redirect_uri")]
    public string? RedirectUri { get; set; }
    
    [FromForm(Name = "client_id")]
    public string? ClientId { get; set; }
    
    [FromForm(Name = "code_verifier")]
    public string? CodeVerifier { get; set; }
    
    [FromForm(Name = "refresh_token")]
    public string? RefreshToken { get; set; }
}

// Models/TokenResponse.cs
public class TokenResponse
{
    [JsonPropertyName("access_token")]
    public string AccessToken { get; set; } = "";
    
    [JsonPropertyName("token_type")]
    public string TokenType { get; set; } = "Bearer";
    
    [JsonPropertyName("expires_in")]
    public int ExpiresIn { get; set; }
    
    [JsonPropertyName("refresh_token")]
    public string? RefreshToken { get; set; }
    
    [JsonPropertyName("scope")]
    public string? Scope { get; set; }
    
    [JsonPropertyName("me")]
    public string Me { get; set; } = "";
}

// Models/TokenErrorResponse.cs
public class TokenErrorResponse
{
    [JsonPropertyName("error")]
    public string Error { get; set; } = "";
    
    [JsonPropertyName("error_description")]
    public string? ErrorDescription { get; set; }
}

// Models/IntrospectionResponse.cs
public class IntrospectionResponse
{
    [JsonPropertyName("active")]
    public bool Active { get; set; }
    
    [JsonPropertyName("me")]
    public string? Me { get; set; }
    
    [JsonPropertyName("client_id")]
    public string? ClientId { get; set; }
    
    [JsonPropertyName("scope")]
    public string? Scope { get; set; }
    
    [JsonPropertyName("exp")]
    public long? Exp { get; set; }
    
    [JsonPropertyName("iat")]
    public long? Iat { get; set; }
}
```

### Database Entity

```csharp
// Data/Entities/RefreshToken.cs
public class RefreshToken
{
    [Key]
    public string Token { get; set; } = "";
    
    public string UserId { get; set; } = "";  // The 'me' URL
    public string ClientId { get; set; } = "";
    public string? Scope { get; set; }
    
    public DateTime CreatedAt { get; set; }
    public DateTime ExpiresAt { get; set; }
    
    public bool Revoked { get; set; }
    public DateTime? RevokedAt { get; set; }
}
```

## JWT Structure

Access tokens are JWTs with the following structure:

### Header
```json
{
  "alg": "HS256",
  "typ": "JWT"
}
```

### Payload
```json
{
  "me": "https://example.com/",
  "client_id": "https://app.example.com/",
  "scope": "profile email",
  "iat": 1640000000,
  "exp": 1640000900,
  "iss": "https://example.com/",
  "aud": "https://example.com/"
}
```

## Configuration

### appsettings.json

```json
{
  "Jwt": {
    "Issuer": "https://example.com/",
    "Audience": "https://example.com/",
    "SecretKey": "your-256-bit-secret-key-minimum-32-characters",
    "AccessTokenExpirationMinutes": 15
  },
  "IndieAuth": {
    "AuthorizationCodeExpirationMinutes": 10,
    "RefreshTokenExpirationDays": 30
  }
}
```

### Configuration Classes

```csharp
// Configuration/JwtSettings.cs
public class JwtSettings
{
    public string Issuer { get; set; } = "";
    public string Audience { get; set; } = "";
    public string SecretKey { get; set; } = "";
    public int AccessTokenExpirationMinutes { get; set; } = 15;
}

// Configuration/IndieAuthSettings.cs
public class IndieAuthSettings
{
    public int AuthorizationCodeExpirationMinutes { get; set; } = 10;
    public int RefreshTokenExpirationDays { get; set; } = 30;
}
```

## Error Responses

| Error Code | HTTP Status | Description |
|------------|-------------|-------------|
| `invalid_request` | 400 | Missing required parameter |
| `invalid_grant` | 400 | Code/token invalid, expired, or mismatched |
| `unsupported_grant_type` | 400 | Grant type not supported |
| `invalid_client` | 401 | Client authentication failed |

## Security Considerations

1. **Short-lived Access Tokens**: JWT expires in 15 minutes
2. **Secure Refresh Tokens**: Cryptographically random, stored in database
3. **Token Revocation**: Refresh tokens can be revoked immediately
4. **PKCE Verification**: Always verify code_verifier against stored challenge
5. **Single-use Codes**: Authorization codes marked as used after exchange
6. **HTTPS Only**: Token endpoint must use HTTPS
7. **No Token in URL**: Tokens only in POST body, never in URL

## Testing

```bash
# Exchange authorization code for tokens
curl -X POST http://localhost:5000/token \
  -H "Content-Type: application/x-www-form-urlencoded" \
  -d "grant_type=authorization_code" \
  -d "code=abc123" \
  -d "client_id=https://app.example.com/" \
  -d "redirect_uri=https://app.example.com/callback" \
  -d "code_verifier=dBjftJeZ4CVP-mB92K27uhbUJU1p1r_wW1gFWFOEjXk"

# Refresh access token
curl -X POST http://localhost:5000/token \
  -H "Content-Type: application/x-www-form-urlencoded" \
  -d "grant_type=refresh_token" \
  -d "refresh_token=dGhpcyBpcyBhbiBvcGFxdWUgdG9rZW4..." \
  -d "client_id=https://app.example.com/"

# Revoke refresh token
curl -X POST http://localhost:5000/token/revoke \
  -H "Content-Type: application/x-www-form-urlencoded" \
  -d "token=dGhpcyBpcyBhbiBvcGFxdWUgdG9rZW4..."

# Introspect token
curl -X POST http://localhost:5000/token/introspect \
  -H "Content-Type: application/x-www-form-urlencoded" \
  -d "token=eyJhbGciOiJIUzI1NiIs..."
```

## Next Steps

After implementing the token endpoint:
1. Implement user authentication → [05-authentication.md](./05-authentication.md)
2. Implement PKCE validation → [06-security.md](./06-security.md)

