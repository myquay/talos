using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using Talos.Web.Configuration;
using Talos.Web.Models;
using Talos.Web.Services;
using Talos.Web.Telemetry;

namespace Talos.Web.Controllers;

[ApiController]
[Route("auth")]
[EnableRateLimiting("auth")]
public class AuthController(
    IAuthorizationService authorizationService,
    IOptions<TalosSettings> talosSettings,
    IAuthTelemetry telemetry)
    : ControllerBase
{
    /// <summary>
    /// IndieAuth Authorization Endpoint
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> Authorize(
        [FromQuery(Name = "response_type")] string responseType,
        [FromQuery(Name = "client_id")] string clientId,
        [FromQuery(Name = "redirect_uri")] string redirectUri,
        [FromQuery] string state,
        [FromQuery(Name = "code_challenge")] string? codeChallenge,
        [FromQuery(Name = "code_challenge_method")] string? codeChallengeMethod,
        [FromQuery] string? scope,
        [FromQuery] string? me)
    {
        var request = new AuthorizationRequest
        {
            ResponseType = responseType,
            ClientId = clientId,
            RedirectUri = redirectUri,
            State = state,
            CodeChallenge = codeChallenge,
            CodeChallengeMethod = codeChallengeMethod,
            Scope = scope,
            Me = me
        };

        var result = await authorizationService.CreateAuthorizationAsync(request);

        if (!result.Success)
        {
            telemetry.TrackAuthorizationDenied(clientId, result.Error ?? "unknown");

            // If redirect_uri is untrusted (invalid/malicious), never redirect to it — show error page directly
            if (!result.RedirectUriUntrusted && !string.IsNullOrEmpty(redirectUri) && Uri.TryCreate(redirectUri, UriKind.Absolute, out _))
            {
                var errorRedirect = $"{redirectUri}?error={result.Error}&error_description={Uri.EscapeDataString(result.ErrorDescription ?? "")}&state={Uri.EscapeDataString(state ?? "")}";
                return Redirect(errorRedirect);
            }
            
            return Redirect($"/error?error={result.Error}&error_description={Uri.EscapeDataString(result.ErrorDescription ?? "")}");
        }

        telemetry.TrackAuthorizationStarted(clientId, scope, responseType);
        return Redirect(result.RedirectUrl!);
    }

    /// <summary>
    /// IndieAuth Authorization Code Verification (profile-only, no access token)
    /// Per spec: clients that do not need an access token exchange the code at the authorization endpoint.
    /// https://indieauth.spec.indieweb.org/#authorization-code-verification
    /// </summary>
    [HttpPost]
    [Consumes("application/x-www-form-urlencoded")]
    public async Task<IActionResult> VerifyAuthorizationCode(
        [FromForm(Name = "grant_type")] string grantType,
        [FromForm(Name = "code")] string? code,
        [FromForm(Name = "client_id")] string? clientId,
        [FromForm(Name = "redirect_uri")] string? redirectUri,
        [FromForm(Name = "code_verifier")] string? codeVerifier)
    {
        if (grantType != "authorization_code")
        {
            return BadRequest(new { error = "unsupported_grant_type" });
        }

        if (string.IsNullOrEmpty(code) || string.IsNullOrEmpty(clientId) ||
            string.IsNullOrEmpty(redirectUri) || string.IsNullOrEmpty(codeVerifier))
        {
            return BadRequest(new
            {
                error = "invalid_request",
                error_description = "code, client_id, redirect_uri, and code_verifier are required"
            });
        }

        var authCode = await authorizationService.ValidateAuthorizationCodeAsync(
            code, clientId, redirectUri, codeVerifier);

        if (authCode == null)
        {
            return BadRequest(new
            {
                error = "invalid_grant",
                error_description = "Invalid, expired, or already used authorization code"
            });
        }

        // Per IndieAuth spec, the authorization endpoint returns only the profile URL (no tokens)
        return Ok(new { me = authCode.ProfileUrl });
    }

    /// <summary>
    /// Get discovered providers for a session
    /// </summary>
    [HttpGet]
    [Route("/api/auth/providers")]
    public async Task<IActionResult> GetProviders([FromQuery(Name = "session_id")] string sessionId)
    {
        var pending = await authorizationService.GetPendingAuthenticationAsync(sessionId);
        if (pending == null)
        {
            return NotFound(new { error = "Session not found or expired" });
        }

        return Ok(new
        {
            profileUrl = pending.ProfileUrl,
            providers = pending.Providers.Select(p => new
            {
                type = p.Type,
                name = p.Name,
                profileUrl = p.ProfileUrl,
                iconUrl = p.IconUrl
            })
        });
    }

    /// <summary>
    /// Select an identity provider and redirect to OAuth
    /// </summary>
    [HttpPost]
    [Route("/api/auth/select-provider")]
    public async Task<IActionResult> SelectProvider([FromBody] SelectProviderRequest request)
    {
        var pending = await authorizationService.GetPendingAuthenticationAsync(request.SessionId);
        if (pending == null)
        {
            return NotFound(new { error = "Session not found or expired" });
        }

        var provider = pending.Providers.FirstOrDefault(p => 
            p.Type.Equals(request.ProviderType, StringComparison.OrdinalIgnoreCase));
        
        if (provider == null)
        {
            return BadRequest(new { error = "Invalid provider" });
        }

        // Generate provider state and store it
        var providerState = Guid.NewGuid().ToString("N");
        pending.SelectedProviderType = provider.Type;
        pending.ProviderState = providerState;
        await authorizationService.UpdatePendingAuthenticationAsync(pending);

        // Build GitHub OAuth URL
        var redirectUri = $"{talosSettings.Value.BaseUrl}/callback/github";
        var authUrl = $"https://github.com/login/oauth/authorize?" +
            $"client_id={Uri.EscapeDataString(GetGitHubClientId())}" +
            $"&redirect_uri={Uri.EscapeDataString(redirectUri)}" +
            $"&state={providerState}" +
            $"&scope=read:user%20user:email" +
            $"&allow_signup=false";

        return Ok(new { redirectUrl = authUrl });
    }

    /// <summary>
    /// Get consent information for a session
    /// </summary>
    [HttpGet]
    [Route("/api/auth/consent")]
    public async Task<IActionResult> GetConsentInfo([FromQuery(Name = "session_id")] string sessionId)
    {
        var pending = await authorizationService.GetPendingAuthenticationAsync(sessionId);
        if (pending == null)
        {
            return NotFound(new { error = "Session not found or expired" });
        }

        if (!pending.IsAuthenticated)
        {
            return BadRequest(new { error = "Not authenticated yet" });
        }

        // Extract client info from client_id URL (use discovered name if available)
        var clientName = pending.ClientName ?? new Uri(pending.ClientId).Host;

        return Ok(new
        {
            client = new
            {
                clientId = pending.ClientId,
                name = clientName,
                url = pending.ClientId,
                logoUrl = pending.ClientLogoUri
            },
            scopes = pending.Scopes,
            profileUrl = pending.ProfileUrl
        });
    }

    /// <summary>
    /// Submit consent decision
    /// </summary>
    [HttpPost]
    [Route("/api/auth/consent")]
    public async Task<IActionResult> SubmitConsent([FromBody] ConsentRequest request)
    {
        var pending = await authorizationService.GetPendingAuthenticationAsync(request.SessionId);
        if (pending == null)
        {
            return NotFound(new { error = "Session not found or expired" });
        }

        if (!pending.IsAuthenticated)
        {
            return BadRequest(new { error = "Not authenticated yet" });
        }

        if (!request.Approved)
        {
            // User denied - redirect with error
            var denyRedirect = $"{pending.RedirectUri}?error=access_denied&error_description=User%20denied%20the%20request&state={Uri.EscapeDataString(pending.State)}";
            return Ok(new { redirectUrl = denyRedirect });
        }

        // User approved - create authorization code
        pending.IsConsentGiven = true;
        await authorizationService.UpdatePendingAuthenticationAsync(pending);

        var codeResult = await authorizationService.CreateAuthorizationCodeAsync(request.SessionId);
        if (!codeResult.Success)
        {
            return BadRequest(new { error = codeResult.Error });
        }

        return Ok(new { redirectUrl = codeResult.RedirectUrl });
    }

    private string GetGitHubClientId()
    {
        // This is a simple approach - could inject IOptions<GitHubSettings> instead
        return HttpContext.RequestServices.GetRequiredService<IOptions<GitHubSettings>>().Value.ClientId;
    }
}

public class SelectProviderRequest
{
    public string SessionId { get; set; } = "";
    public string ProviderType { get; set; } = "";
}

public class ConsentRequest
{
    public string SessionId { get; set; } = "";
    public bool Approved { get; set; }
}


