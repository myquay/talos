namespace Talos.Web.Configuration;

public class TalosSettings
{
    public string BaseUrl { get; set; } = "";
    
    /// <summary>
    /// Optional list of allowed profile hosts. When configured, only users whose 'me' URL 
    /// matches one of these hosts can authenticate. Leave null or empty to allow all hosts.
    /// Matching is case-insensitive and exact (no wildcard/subdomain support).
    /// </summary>
    public string[]? AllowedProfileHosts { get; set; }

    /// <summary>
    /// Exact browser origins allowed to use the public-client token endpoints.
    /// </summary>
    public string[] AllowedClientOrigins { get; set; } = [];
}
