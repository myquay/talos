namespace Talos.Web.Data.Entities;

public class PendingAuthenticationEntity
{
    public string SessionId { get; set; } = "";
    public string ClientId { get; set; } = "";
    public string RedirectUri { get; set; } = "";
    public string State { get; set; } = "";
    public string? CodeChallenge { get; set; }
    public string? CodeChallengeMethod { get; set; }
    public string? Scopes { get; set; }
    public string ProfileUrl { get; set; } = "";
    public string? ProvidersJson { get; set; }
    public string? SelectedProviderType { get; set; }
    public string? ProviderState { get; set; }
    public bool IsAuthenticated { get; set; }
    public bool IsConsentGiven { get; set; }
    public string? ClientName { get; set; }
    public string? ClientLogoUri { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime ExpiresAt { get; set; }
}

public class AuthorizationCodeEntity
{
    public string Code { get; set; } = "";
    public string ClientId { get; set; } = "";
    public string RedirectUri { get; set; } = "";
    public string ProfileUrl { get; set; } = "";
    public string? Scopes { get; set; }
    public string? CodeChallenge { get; set; }
    public string? CodeChallengeMethod { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime ExpiresAt { get; set; }
    public bool IsUsed { get; set; }
}

public class RefreshTokenEntity
{
    public string Token { get; set; } = "";
    public string ProfileUrl { get; set; } = "";
    public string ClientId { get; set; } = "";
    public string? Scopes { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime ExpiresAt { get; set; }
    public bool IsRevoked { get; set; }
}

