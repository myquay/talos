using Talos.Web.Models;

namespace Talos.Web.Services;

/// <summary>
/// Service for fetching and parsing client metadata from a client_id URL.
/// Supports both JSON metadata documents (§4.2.1) and HTML h-app microformats (§4.2.2).
/// </summary>
public interface IClientDiscoveryService
{
    /// <summary>
    /// Fetches and parses client metadata from the client_id URL.
    /// Returns a default ClientInfo (with just the client_id) if the URL
    /// cannot be fetched, is localhost, or returns unrecognized content.
    /// </summary>
    Task<ClientInfo> DiscoverClientAsync(string clientId);
}
