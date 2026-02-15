namespace Talos.Web.Configuration;

public class IndieAuthSettings
{
    public int AuthorizationCodeExpirationMinutes { get; set; } = 10;
    public int RefreshTokenExpirationDays { get; set; } = 30;
    public int PendingAuthenticationExpirationMinutes { get; set; } = 30;

    /// <summary>
    /// Shared secret that resource servers must present as a Bearer token
    /// when calling the introspection endpoint. MUST be configured for
    /// introspection to function. If null/empty, all introspection requests
    /// are rejected with 401 (fail-closed).
    /// </summary>
    public string? IntrospectionSecret { get; set; }
}

