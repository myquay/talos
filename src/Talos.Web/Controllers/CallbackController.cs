using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Talos.Web.Configuration;
using Talos.Web.Services;
using Talos.Web.Services.IdentityProviders;

namespace Talos.Web.Controllers;

[ApiController]
[Route("callback")]
public class CallbackController(
    IAuthorizationService authorizationService,
    IIdentityProviderFactory providerFactory,
    IOptions<TalosSettings> talosSettings,
    ILogger<CallbackController> logger)
    : ControllerBase
{

    /// <summary>
    /// GitHub OAuth callback
    /// </summary>
    [HttpGet("github")]
    public async Task<IActionResult> GitHubCallback(
        [FromQuery] string? code,
        [FromQuery] string? state,
        [FromQuery] string? error,
        [FromQuery(Name = "error_description")] string? errorDescription)
    {
        // Handle OAuth errors
        if (!string.IsNullOrEmpty(error))
        {
            logger.LogWarning("GitHub OAuth error: {Error} - {Description}", error, errorDescription);
            return Redirect($"/error?error={error}&error_description={Uri.EscapeDataString(errorDescription ?? "")}");
        }

        if (string.IsNullOrEmpty(code) || string.IsNullOrEmpty(state))
        {
            return Redirect("/error?error=invalid_request&error_description=Missing%20code%20or%20state");
        }

        // Find pending authentication by provider state
        var pending = await FindPendingByProviderStateAsync(state);
        if (pending == null)
        {
            logger.LogWarning("No pending authentication found for state: {State}", state);
            return Redirect("/error?error=invalid_state&error_description=Session%20expired%20or%20invalid");
        }

        // Get GitHub provider
        var provider = providerFactory.GetProviderByType("github");
        if (provider == null)
        {
            logger.LogError("GitHub provider not registered");
            return Redirect("/error?error=server_error&error_description=Provider%20not%20available");
        }

        // Exchange code for token
        var redirectUri = $"{talosSettings.Value.BaseUrl}/callback/github";
        var tokenResult = await provider.ExchangeCodeAsync(code, redirectUri);
        if (!tokenResult.Success)
        {
            logger.LogWarning("GitHub token exchange failed: {Error}", tokenResult.Error);
            return Redirect($"/error?error=token_exchange_failed&error_description={Uri.EscapeDataString(tokenResult.Error ?? "Token exchange failed")}");
        }

        // Find the expected GitHub profile from discovered providers
        var selectedProvider = pending.Providers.FirstOrDefault(p => 
            p.Type.Equals("github", StringComparison.OrdinalIgnoreCase));
        
        if (selectedProvider == null)
        {
            return Redirect("/error?error=invalid_provider&error_description=GitHub%20not%20in%20discovered%20providers");
        }

        // Verify the authenticated user matches expected profile
        var verifyResult = await provider.VerifyProfileAsync(
            tokenResult.AccessToken!, 
            selectedProvider.ProfileUrl,
            pending.ProfileUrl);

        if (!verifyResult.Verified)
        {
            logger.LogWarning("GitHub verification failed: {Error}", verifyResult.Error);
            return Redirect($"/error?error=verification_failed&error_description={Uri.EscapeDataString(verifyResult.Error ?? "Verification failed")}");
        }

        // Update pending authentication
        pending.IsAuthenticated = true;
        await authorizationService.UpdatePendingAuthenticationAsync(pending);

        // Redirect to consent page
        return Redirect($"/consent?session_id={pending.SessionId}");
    }

    private async Task<Models.PendingAuthentication?> FindPendingByProviderStateAsync(string providerState)
    {
        // This is a simplified approach - in production you'd want an index on ProviderState
        // For now, we'll scan (not great for scale, but works for MVP)
        // A better approach would be to store provider state -> session mapping separately
        
        // Get all non-expired pending authentications
        // Note: This would need to query the database directly for efficiency
        // For now, we'll use a workaround

        using var scope = HttpContext.RequestServices.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<Data.TalosDbContext>();
        
        var entity = await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions
            .FirstOrDefaultAsync(dbContext.PendingAuthentications,
                p => p.ProviderState == providerState && p.ExpiresAt > DateTime.UtcNow);

        if (entity == null)
            return null;

        return await authorizationService.GetPendingAuthenticationAsync(entity.SessionId);
    }
}

