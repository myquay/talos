namespace Talos.Web.Models;

public class AuthorizationRequest
{
    public string ResponseType { get; set; } = "";
    public string ClientId { get; set; } = "";
    public string RedirectUri { get; set; } = "";
    public string State { get; set; } = "";
    public string? CodeChallenge { get; set; }
    public string? CodeChallengeMethod { get; set; }
    public string? Scope { get; set; }
    public string? Me { get; set; }
}

public class PendingAuthentication
{
    public string SessionId { get; set; } = "";
    public string ClientId { get; set; } = "";
    public string RedirectUri { get; set; } = "";
    public string State { get; set; } = "";
    public string? CodeChallenge { get; set; }
    public string? CodeChallengeMethod { get; set; }
    public List<string> Scopes { get; set; } = new();
    public string ProfileUrl { get; set; } = "";
    public List<DiscoveredProvider> Providers { get; set; } = new();
    public string? SelectedProviderType { get; set; }
    public string? ProviderState { get; set; }
    public bool IsAuthenticated { get; set; }
    public bool IsConsentGiven { get; set; }
    public string? ClientName { get; set; }
    public string? ClientLogoUri { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime ExpiresAt { get; set; }
}

public class AuthorizationCode
{
    public string Code { get; set; } = "";
    public string ClientId { get; set; } = "";
    public string RedirectUri { get; set; } = "";
    public string ProfileUrl { get; set; } = "";
    public List<string> Scopes { get; set; } = new();
    public string? CodeChallenge { get; set; }
    public string? CodeChallengeMethod { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime ExpiresAt { get; set; }
    public bool IsUsed { get; set; }
}

