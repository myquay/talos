namespace Talos.Web.Models;

/// <summary>
/// Discovered client application metadata, fetched from the client_id URL.
/// Can be populated from either a JSON metadata document (§4.2.1) or
/// HTML h-app microformat (§4.2.2).
/// </summary>
public class ClientInfo
{
    public string ClientId { get; set; } = "";
    public string? ClientName { get; set; }
    public string? ClientUri { get; set; }
    public string? LogoUri { get; set; }
    public List<string> RedirectUris { get; set; } = [];

    /// <summary>Whether the client was fetched successfully (false = used defaults).</summary>
    public bool WasFetched { get; set; }
}
