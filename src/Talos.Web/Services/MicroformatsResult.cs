namespace Talos.Web.Services;

/// <summary>
/// Result of parsing microformats data from HTML content.
/// </summary>
public class MicroformatsResult
{
    /// <summary>
    /// List of URLs found in rel="me" links.
    /// </summary>
    public List<string> RelMeLinks { get; set; } = [];

    /// <summary>
    /// The authorization endpoint URL (IndieAuth).
    /// </summary>
    public string? AuthorizationEndpoint { get; set; }

    /// <summary>
    /// The token endpoint URL (IndieAuth).
    /// </summary>
    public string? TokenEndpoint { get; set; }

    /// <summary>
    /// The IndieAuth metadata endpoint URL.
    /// </summary>
    public string? IndieAuthMetadata { get; set; }

    /// <summary>
    /// The Micropub endpoint URL.
    /// </summary>
    public string? Micropub { get; set; }

    /// <summary>
    /// The Microsub endpoint URL.
    /// </summary>
    public string? Microsub { get; set; }
}

