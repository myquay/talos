using Talos.Web.Models;

namespace Talos.Web.Services;

public interface IAuthorizationService
{
    Task<AuthorizationResult> CreateAuthorizationAsync(AuthorizationRequest request);
    Task<PendingAuthentication?> GetPendingAuthenticationAsync(string sessionId);
    Task UpdatePendingAuthenticationAsync(PendingAuthentication authentication);
    Task<AuthorizationCodeResult> CreateAuthorizationCodeAsync(string sessionId);
    Task<AuthorizationCode?> ValidateAuthorizationCodeAsync(string code, string clientId, string redirectUri, string codeVerifier);
}

public class AuthorizationResult
{
    public bool Success { get; set; }
    public string? SessionId { get; set; }
    public string? Error { get; set; }
    public string? ErrorDescription { get; set; }
    public string? RedirectUrl { get; set; }
}

public class AuthorizationCodeResult
{
    public bool Success { get; set; }
    public string? Code { get; set; }
    public string? RedirectUrl { get; set; }
    public string? Error { get; set; }
}

