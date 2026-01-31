namespace Talos.Web.Services;

public interface ITokenService
{
    string GenerateAccessToken(string profileUrl, string clientId, IEnumerable<string> scopes);
    string GenerateRefreshToken();
    Task<TokenValidationResult> ValidateAccessTokenAsync(string token);
}

public class TokenValidationResult
{
    public bool IsValid { get; set; }
    public string? ProfileUrl { get; set; }
    public string? ClientId { get; set; }
    public IEnumerable<string> Scopes { get; set; } = Array.Empty<string>();
    public DateTime? ExpiresAt { get; set; }
    public string? Error { get; set; }
}

