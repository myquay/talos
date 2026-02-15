using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Talos.Web.Configuration;
using Talos.Web.Data;
using Talos.Web.Data.Entities;
using Talos.Web.Services;

namespace Talos.Web.Controllers;

[ApiController]
[Route("token")]
[EnableRateLimiting("token")]
public class TokenController(
    IAuthorizationService authorizationService,
    ITokenService tokenService,
    TalosDbContext dbContext,
    IOptions<IndieAuthSettings> settings)
    : ControllerBase
{
    /// <summary>
    /// IndieAuth Token Endpoint
    /// </summary>
    [HttpPost]
    [Consumes("application/x-www-form-urlencoded")]
    public async Task<IActionResult> Exchange([FromForm] TokenRequest request)
    {
        if (request.GrantType == "authorization_code")
        {
            return await HandleAuthorizationCodeGrant(request);
        }
        else if (request.GrantType == "refresh_token")
        {
            return await HandleRefreshTokenGrant(request);
        }
        else
        {
            return BadRequest(new TokenErrorResponse
            {
                Error = "unsupported_grant_type",
                ErrorDescription = "Only authorization_code and refresh_token grant types are supported"
            });
        }
    }

    private async Task<IActionResult> HandleAuthorizationCodeGrant(TokenRequest request)
    {
        if (string.IsNullOrEmpty(request.Code))
        {
            return BadRequest(new TokenErrorResponse
            {
                Error = "invalid_request",
                ErrorDescription = "code is required"
            });
        }

        if (string.IsNullOrEmpty(request.ClientId))
        {
            return BadRequest(new TokenErrorResponse
            {
                Error = "invalid_request",
                ErrorDescription = "client_id is required"
            });
        }

        if (string.IsNullOrEmpty(request.RedirectUri))
        {
            return BadRequest(new TokenErrorResponse
            {
                Error = "invalid_request",
                ErrorDescription = "redirect_uri is required"
            });
        }

        if (string.IsNullOrEmpty(request.CodeVerifier))
        {
            return BadRequest(new TokenErrorResponse
            {
                Error = "invalid_request",
                ErrorDescription = "code_verifier is required (PKCE)"
            });
        }

        var authCode = await authorizationService.ValidateAuthorizationCodeAsync(
            request.Code,
            request.ClientId,
            request.RedirectUri,
            request.CodeVerifier);

        if (authCode == null)
        {
            return BadRequest(new TokenErrorResponse
            {
                Error = "invalid_grant",
                ErrorDescription = "Invalid, expired, or already used authorization code"
            });
        }

        // Per IndieAuth §5.3.3 — codes with no scope cannot be exchanged for an access token.
        // The client should exchange scope-less codes at the authorization endpoint instead.
        if (authCode.Scopes == null || authCode.Scopes.Count == 0)
        {
            return BadRequest(new TokenErrorResponse
            {
                Error = "invalid_grant",
                ErrorDescription = "This authorization code was issued with no scope and cannot be exchanged for an access token. Use the authorization endpoint instead."
            });
        }

        // Generate tokens
        var accessToken = tokenService.GenerateAccessToken(
            authCode.ProfileUrl,
            authCode.ClientId,
            authCode.Scopes);

        var refreshToken = tokenService.GenerateRefreshToken();

        // Store refresh token
        dbContext.RefreshTokens.Add(new RefreshTokenEntity
        {
            Token = refreshToken,
            ProfileUrl = authCode.ProfileUrl,
            ClientId = authCode.ClientId,
            Scopes = string.Join(" ", authCode.Scopes),
            CreatedAt = DateTime.UtcNow,
            ExpiresAt = DateTime.UtcNow.AddDays(settings.Value.RefreshTokenExpirationDays)
        });
        await dbContext.SaveChangesAsync();

        return Ok(new TokenResponse
        {
            AccessToken = accessToken,
            TokenType = "Bearer",
            ExpiresIn = 15 * 60, // 15 minutes in seconds
            RefreshToken = refreshToken,
            Scope = string.Join(" ", authCode.Scopes),
            Me = authCode.ProfileUrl
        });
    }

    private async Task<IActionResult> HandleRefreshTokenGrant(TokenRequest request)
    {
        if (string.IsNullOrEmpty(request.RefreshToken))
        {
            return BadRequest(new TokenErrorResponse
            {
                Error = "invalid_request",
                ErrorDescription = "refresh_token is required"
            });
        }

        // client_id is required per IndieAuth spec §5.3.3 (REFRESH-2 / GAP-13)
        if (string.IsNullOrEmpty(request.ClientId))
        {
            return BadRequest(new TokenErrorResponse
            {
                Error = "invalid_request",
                ErrorDescription = "client_id is required"
            });
        }

        var storedToken = await dbContext.RefreshTokens
            .FirstOrDefaultAsync(t => t.Token == request.RefreshToken && 
                                      !t.IsRevoked && 
                                      t.ExpiresAt > DateTime.UtcNow);

        if (storedToken == null)
        {
            return BadRequest(new TokenErrorResponse
            {
                Error = "invalid_grant",
                ErrorDescription = "Invalid or expired refresh token"
            });
        }

        // Verify client_id matches the token's bound client
        if (storedToken.ClientId != request.ClientId)
        {
            return BadRequest(new TokenErrorResponse
            {
                Error = "invalid_grant",
                ErrorDescription = "client_id mismatch"
            });
        }

        // Rotate refresh token
        storedToken.IsRevoked = true;
        
        var newRefreshToken = tokenService.GenerateRefreshToken();
        var scopes = storedToken.Scopes?.Split(' ', StringSplitOptions.RemoveEmptyEntries) ?? Array.Empty<string>();
        
        dbContext.RefreshTokens.Add(new RefreshTokenEntity
        {
            Token = newRefreshToken,
            ProfileUrl = storedToken.ProfileUrl,
            ClientId = storedToken.ClientId,
            Scopes = storedToken.Scopes,
            CreatedAt = DateTime.UtcNow,
            ExpiresAt = DateTime.UtcNow.AddDays(settings.Value.RefreshTokenExpirationDays)
        });

        await dbContext.SaveChangesAsync();

        // Generate new access token
        var accessToken = tokenService.GenerateAccessToken(
            storedToken.ProfileUrl,
            storedToken.ClientId,
            scopes);

        return Ok(new TokenResponse
        {
            AccessToken = accessToken,
            TokenType = "Bearer",
            ExpiresIn = 15 * 60,
            RefreshToken = newRefreshToken,
            Scope = storedToken.Scopes,
            Me = storedToken.ProfileUrl
        });
    }

    /// <summary>
    /// Token introspection endpoint (RFC 7662)
    /// </summary>
    [HttpPost("introspect")]
    [Consumes("application/x-www-form-urlencoded")]
    public async Task<IActionResult> Introspect([FromForm] string token)
    {
        // Per IndieAuth §6.1 — introspection MUST require authorization
        if (!IsIntrospectionAuthorized())
        {
            return Unauthorized(new { error = "unauthorized", error_description = "Bearer token required" });
        }

        if (string.IsNullOrEmpty(token))
        {
            return Ok(new IntrospectionResponse { Active = false });
        }

        var result = await tokenService.ValidateAccessTokenAsync(token);
        
        if (!result.IsValid)
        {
            return Ok(new IntrospectionResponse { Active = false });
        }

        return Ok(new IntrospectionResponse
        {
            Active = true,
            Me = result.ProfileUrl,
            ClientId = result.ClientId,
            Scope = string.Join(" ", result.Scopes),
            Exp = new DateTimeOffset(result.ExpiresAt!.Value).ToUnixTimeSeconds()
        });
    }

    /// <summary>
    /// Validates the Authorization header against the configured introspection secret.
    /// Per IndieAuth §6.1 and RFC 7662, introspection MUST require authorization.
    /// Uses constant-time comparison to prevent timing attacks.
    /// </summary>
    private bool IsIntrospectionAuthorized()
    {
        var secret = settings.Value.IntrospectionSecret;

        // Fail closed: if no secret is configured, reject all requests
        if (string.IsNullOrEmpty(secret))
            return false;

        var authHeader = Request.Headers.Authorization.ToString();
        if (string.IsNullOrEmpty(authHeader))
            return false;

        // Expect "Bearer <secret>"
        if (!authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            return false;

        var provided = authHeader["Bearer ".Length..].Trim();
        if (string.IsNullOrEmpty(provided))
            return false;

        // Constant-time comparison to prevent timing attacks
        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(provided),
            Encoding.UTF8.GetBytes(secret));
    }

    /// <summary>
    /// Token revocation endpoint (RFC 7009)
    /// </summary>
    [HttpPost("revoke")]
    [Consumes("application/x-www-form-urlencoded")]
    public async Task<IActionResult> Revoke([FromForm] string token)
    {
        if (string.IsNullOrEmpty(token))
        {
            return Ok(); // RFC 7009: Always return 200
        }

        // Try to find and revoke a refresh token
        var refreshToken = await dbContext.RefreshTokens
            .FirstOrDefaultAsync(t => t.Token == token);

        if (refreshToken != null)
        {
            refreshToken.IsRevoked = true;
            await dbContext.SaveChangesAsync();
        }

        // Note: Access tokens are JWTs and can't be revoked directly
        // They will simply expire. For stricter revocation, use token introspection
        
        return Ok();
    }
}

public class TokenRequest
{
    [FromForm(Name = "grant_type")]
    public string GrantType { get; set; } = "";
    
    [FromForm(Name = "code")]
    public string? Code { get; set; }
    
    [FromForm(Name = "client_id")]
    public string? ClientId { get; set; }
    
    [FromForm(Name = "redirect_uri")]
    public string? RedirectUri { get; set; }
    
    [FromForm(Name = "code_verifier")]
    public string? CodeVerifier { get; set; }
    
    [FromForm(Name = "refresh_token")]
    public string? RefreshToken { get; set; }
}

public class TokenResponse
{
    public string AccessToken { get; set; } = "";
    public string TokenType { get; set; } = "Bearer";
    public int ExpiresIn { get; set; }
    public string? RefreshToken { get; set; }
    public string? Scope { get; set; }
    public string Me { get; set; } = "";
}

public class TokenErrorResponse
{
    public string Error { get; set; } = "";
    public string? ErrorDescription { get; set; }
}

/// <summary>
/// Token introspection response per RFC 7662 §2.2 and IndieAuth §6.2.
/// Uses explicit [JsonPropertyName] attributes to guarantee correct wire format
/// regardless of any PropertyNamingPolicy configured on the application.
/// </summary>
public class IntrospectionResponse
{
    /// <summary>
    /// Boolean indicator of whether or not the presented token is currently active.
    /// MUST be a JSON boolean — never a string.
    /// </summary>
    [JsonPropertyName("active")]
    public bool Active { get; set; }

    [JsonPropertyName("me")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Me { get; set; }

    [JsonPropertyName("client_id")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ClientId { get; set; }

    [JsonPropertyName("scope")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Scope { get; set; }

    [JsonPropertyName("exp")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public long Exp { get; set; }
}


