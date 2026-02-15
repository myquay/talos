using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Talos.Web.Configuration;

namespace Talos.Web.Controllers;

[ApiController]
public class MetadataController(IOptions<TalosSettings> talosSettings) : ControllerBase
{
    private readonly TalosSettings _talosSettings = talosSettings.Value;

    /// <summary>
    /// IndieAuth Server Metadata (RFC 8414 / IndieAuth)
    /// </summary>
    [HttpGet("/.well-known/oauth-authorization-server")]
    public IActionResult GetMetadata()
    {
        var baseUrl = _talosSettings.BaseUrl.TrimEnd('/');
        
        return Ok(new
        {
            issuer = baseUrl,
            authorization_endpoint = $"{baseUrl}/auth",
            token_endpoint = $"{baseUrl}/token",
            introspection_endpoint = $"{baseUrl}/token/introspect",
            revocation_endpoint = $"{baseUrl}/token/revoke",
            code_challenge_methods_supported = new[] { "S256" },
            grant_types_supported = new[] { "authorization_code", "refresh_token" },
            response_types_supported = new[] { "code" },
            scopes_supported = new[] { "profile", "email", "create", "update", "delete", "media" },
            service_documentation = "https://indieauth.spec.indieweb.org/",
            introspection_endpoint_auth_methods_supported = new[] { "Bearer" }
        });
    }

    /// <summary>
    /// IndieAuth Metadata (alternative path)
    /// </summary>
    [HttpGet("/.well-known/indieauth-server")]
    public IActionResult GetIndieAuthMetadata()
    {
        return GetMetadata();
    }
}

