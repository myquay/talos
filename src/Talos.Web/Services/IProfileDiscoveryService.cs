using Talos.Web.Models;

namespace Talos.Web.Services;

public interface IProfileDiscoveryService
{
    Task<ProfileDiscoveryResult> DiscoverProfileAsync(string profileUrl);
}

public class ProfileDiscoveryResult
{
    public string ProfileUrl { get; init; } = "";
    public List<DiscoveredProvider> Providers { get; set; } = [];
    public string? AuthorizationEndpoint { get; set; }
    public string? TokenEndpoint { get; set; }
    public bool Success { get; set; }
    public string? Error { get; set; }
}

