using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using Talos.Web.Models;

namespace Talos.Web.Services;

/// <summary>
/// Fetches and parses client metadata from a client_id URL.
/// Supports JSON metadata documents (IndieAuth §4.2.1) and HTML with h-app microformats (§4.2.2).
/// Failures are non-fatal — client discovery is SHOULD-level per spec.
/// </summary>
public class ClientDiscoveryService(
    IHttpClientFactory httpClientFactory,
    IMicroformatsService microformatsService,
    ILogger<ClientDiscoveryService> logger)
    : IClientDiscoveryService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    /// <inheritdoc />
    public async Task<ClientInfo> DiscoverClientAsync(string clientId)
    {
        var defaultInfo = new ClientInfo { ClientId = clientId, WasFetched = false };

        // DISC-6: Must not fetch localhost/loopback client_id URLs
        if (IsLoopback(clientId))
        {
            logger.LogDebug("Skipping client discovery for loopback client_id: {ClientId}", clientId);
            return defaultInfo;
        }

        try
        {
            var client = httpClientFactory.CreateClient("ClientDiscovery");
            using var response = await client.GetAsync(clientId);

            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning(
                    "Client discovery failed for {ClientId}: HTTP {StatusCode}",
                    clientId, (int)response.StatusCode);
                return defaultInfo;
            }

            var contentType = response.Content.Headers.ContentType;
            var body = await response.Content.ReadAsStringAsync();

            if (IsJsonContentType(contentType))
            {
                return ParseJsonMetadata(body, clientId);
            }

            if (IsHtmlContentType(contentType))
            {
                return ParseHtmlMetadata(body, clientId);
            }

            logger.LogWarning(
                "Client discovery for {ClientId} returned unsupported Content-Type: {ContentType}",
                clientId, contentType?.MediaType);
            return defaultInfo;
        }
        catch (TaskCanceledException)
        {
            logger.LogWarning("Client discovery timed out for {ClientId}", clientId);
            return defaultInfo;
        }
        catch (HttpRequestException ex)
        {
            logger.LogWarning(ex, "Client discovery HTTP error for {ClientId}", clientId);
            return defaultInfo;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Client discovery unexpected error for {ClientId}", clientId);
            return defaultInfo;
        }
    }

    /// <summary>
    /// Parses a JSON client metadata document per IndieAuth §4.2.1.
    /// </summary>
    internal ClientInfo ParseJsonMetadata(string json, string clientId)
    {
        var defaultInfo = new ClientInfo { ClientId = clientId, WasFetched = false };

        ClientMetadataDocument? doc;
        try
        {
            doc = JsonSerializer.Deserialize<ClientMetadataDocument>(json, JsonOptions);
        }
        catch (JsonException ex)
        {
            logger.LogWarning(ex, "Failed to parse client JSON metadata for {ClientId}", clientId);
            return defaultInfo;
        }

        if (doc == null)
            return defaultInfo;

        // DISC-8: client_id in document MUST match the request client_id
        if (!string.IsNullOrEmpty(doc.ClientId) &&
            !string.Equals(doc.ClientId, clientId, StringComparison.Ordinal))
        {
            logger.LogWarning(
                "Client metadata client_id mismatch: document has {DocClientId}, expected {ClientId}",
                doc.ClientId, clientId);
            return defaultInfo;
        }

        // DISC-8: client_uri MUST be a prefix of client_id
        if (!string.IsNullOrEmpty(doc.ClientUri) && !clientId.StartsWith(doc.ClientUri, StringComparison.Ordinal))
        {
            logger.LogWarning(
                "Client metadata client_uri {ClientUri} is not a prefix of client_id {ClientId}",
                doc.ClientUri, clientId);
            return defaultInfo;
        }

        return new ClientInfo
        {
            ClientId = clientId,
            ClientName = doc.ClientName,
            ClientUri = doc.ClientUri,
            LogoUri = doc.LogoUri,
            RedirectUris = doc.RedirectUris ?? [],
            WasFetched = true
        };
    }

    /// <summary>
    /// Parses HTML for h-app microformat data per IndieAuth §4.2.2.
    /// </summary>
    internal ClientInfo ParseHtmlMetadata(string html, string clientId)
    {
        if (!Uri.TryCreate(clientId, UriKind.Absolute, out var baseUrl))
            return new ClientInfo { ClientId = clientId, WasFetched = true };

        var mfResult = microformatsService.Parse(html, baseUrl);

        return new ClientInfo
        {
            ClientId = clientId,
            ClientName = mfResult.AppName,
            LogoUri = mfResult.AppLogoUrl,
            ClientUri = mfResult.AppUrl,
            // HTML documents don't provide redirect_uri lists — that's JSON-only
            RedirectUris = [],
            WasFetched = true
        };
    }

    /// <summary>
    /// Checks if a client_id URL points to localhost or a loopback address.
    /// Per DISC-6, the server MUST NOT fetch these URLs.
    /// </summary>
    internal static bool IsLoopback(string clientId)
    {
        if (!Uri.TryCreate(clientId, UriKind.Absolute, out var uri))
            return false;

        var host = uri.Host;
        return string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase)
               || host == "127.0.0.1"
               || host == "[::1]"
               || host == "::1";
    }

    private static bool IsJsonContentType(MediaTypeHeaderValue? contentType)
    {
        if (contentType == null) return false;
        var mediaType = contentType.MediaType;
        return string.Equals(mediaType, "application/json", StringComparison.OrdinalIgnoreCase)
               || (mediaType != null && mediaType.EndsWith("+json", StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsHtmlContentType(MediaTypeHeaderValue? contentType)
    {
        if (contentType == null) return false;
        var mediaType = contentType.MediaType;
        return string.Equals(mediaType, "text/html", StringComparison.OrdinalIgnoreCase)
               || string.Equals(mediaType, "application/xhtml+xml", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// DTO for deserializing the JSON client metadata document (§4.2.1).
    /// </summary>
    internal class ClientMetadataDocument
    {
        [JsonPropertyName("client_id")]
        public string? ClientId { get; set; }
        
        [JsonPropertyName("client_name")]
        public string? ClientName { get; set; }
        
        [JsonPropertyName("client_uri")]
        public string? ClientUri { get; set; }
        
        [JsonPropertyName("logo_uri")]
        public string? LogoUri { get; set; }
        
        [JsonPropertyName("redirect_uris")]
        public List<string>? RedirectUris { get; set; }
    }
}
