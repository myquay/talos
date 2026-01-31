namespace Talos.Web.Services.IdentityProviders;

public interface IIdentityProvider
{
    string ProviderType { get; }
    string DisplayName { get; }
    bool CanHandle(string url);
    string GetAuthorizationUrl(string state, string redirectUri);
    Task<ProviderAuthResult> ExchangeCodeAsync(string code, string redirectUri);
    Task<ProviderVerificationResult> VerifyProfileAsync(string accessToken, string expectedProfileUrl);
}

public class ProviderAuthResult
{
    public bool Success { get; set; }
    public string? AccessToken { get; set; }
    public string? Error { get; set; }
    public string? ProviderUserId { get; set; }
    public string? ProviderUsername { get; set; }
    public string? ProviderProfileUrl { get; set; }
    public string? Email { get; set; }
}

public class ProviderVerificationResult
{
    public bool Verified { get; set; }
    public string? Error { get; set; }
    public string? ProfileUrl { get; set; }
    public string? Name { get; set; }
    public string? Email { get; set; }
}

