namespace Talos.Web.Configuration;

public class IndieAuthSettings
{
    private static readonly string[] DefaultAllowedScopes = ["profile", "email", "create", "update", "delete", "media"];

    public int AuthorizationCodeExpirationMinutes { get; set; } = 10;
    public int RefreshTokenExpirationDays { get; set; } = 30;
    public int PendingAuthenticationExpirationMinutes { get; set; } = 30;
    public string[] AllowedScopes { get; set; } = DefaultAllowedScopes;

    /// <summary>
    /// Optional server-side secret for HMAC hashing refresh tokens before
    /// storing them. If not configured, refresh tokens are still stored as
    /// SHA-256 hashes rather than plaintext.
    /// </summary>
    public string? RefreshTokenHashSecret { get; set; }

    /// <summary>
    /// Shared secret that resource servers must present as a Bearer token
    /// when calling the introspection endpoint. MUST be configured for
    /// introspection to function. If null/empty, all introspection requests
    /// are rejected with 401 (fail-closed).
    /// </summary>
    public string? IntrospectionSecret { get; set; }
}
