using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Talos.Web.Configuration;

namespace Talos.Web.Services;

public class TokenService(IOptions<JwtSettings> jwtSettings, ILogger<TokenService> logger)
    : ITokenService
{
    private readonly JwtSettings _jwtSettings = jwtSettings.Value;

    public string GenerateAccessToken(string profileUrl, string clientId, IEnumerable<string> scopes)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_jwtSettings.SecretKey));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var claims = new List<Claim>
        {
            new("me", profileUrl),
            new("client_id", clientId),
            new("scope", string.Join(" ", scopes)),
            new(JwtRegisteredClaimNames.Iat, DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(), ClaimValueTypes.Integer64),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
        };

        var token = new JwtSecurityToken(
            issuer: _jwtSettings.Issuer,
            audience: _jwtSettings.Audience,
            claims: claims,
            expires: DateTime.UtcNow.AddMinutes(_jwtSettings.AccessTokenExpirationMinutes),
            signingCredentials: credentials
        );

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    public string GenerateRefreshToken()
    {
        var randomBytes = new byte[32];
        using var rng = RandomNumberGenerator.Create();
        rng.GetBytes(randomBytes);
        return Convert.ToBase64String(randomBytes);
    }

    public Task<TokenValidationResult> ValidateAccessTokenAsync(string token)
    {
        var result = new TokenValidationResult();

        try
        {
            var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_jwtSettings.SecretKey));
            var handler = new JwtSecurityTokenHandler();

            var validationParameters = new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidateAudience = true,
                ValidateLifetime = true,
                ValidateIssuerSigningKey = true,
                ValidIssuer = _jwtSettings.Issuer,
                ValidAudience = _jwtSettings.Audience,
                IssuerSigningKey = key,
                ClockSkew = TimeSpan.FromMinutes(1)
            };

            _ = handler.ValidateToken(token, validationParameters, out var validatedToken);
            var jwtToken = (JwtSecurityToken)validatedToken;

            result.IsValid = true;
            result.ProfileUrl = jwtToken.Claims.FirstOrDefault(c => c.Type == "me")?.Value;
            result.ClientId = jwtToken.Claims.FirstOrDefault(c => c.Type == "client_id")?.Value;
            result.ExpiresAt = jwtToken.ValidTo;

            var scopeClaim = jwtToken.Claims.FirstOrDefault(c => c.Type == "scope")?.Value;
            result.Scopes = string.IsNullOrEmpty(scopeClaim) 
                ? [] 
                : scopeClaim.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        }
        catch (SecurityTokenExpiredException)
        {
            result.Error = "Token has expired";
        }
        catch (SecurityTokenException ex)
        {
            logger.LogWarning(ex, "Token validation failed");
            result.Error = "Invalid token";
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unexpected error during token validation");
            result.Error = "Token validation failed";
        }

        return Task.FromResult(result);
    }
}

