using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Talos.Web.Configuration;
using Talos.Web.Data;
using Talos.Web.Models;
using Talos.Web.Services.IdentityProviders;

namespace Talos.Web.Services;

public class AuthorizationService(
    TalosDbContext dbContext,
    IProfileDiscoveryService profileDiscovery,
    IPkceService pkceService,
    IIdentityProviderFactory providerFactory,
    IOptions<IndieAuthSettings> settings,
    IOptions<TalosSettings> talosSettings,
    ILogger<AuthorizationService> logger)
    : IAuthorizationService
{
    public async Task<AuthorizationResult> CreateAuthorizationAsync(AuthorizationRequest request)
    {
        var result = new AuthorizationResult();

        // Validate required parameters
        if (request.ResponseType != "code")
        {
            return ErrorResult("unsupported_response_type", "Only 'code' response type is supported");
        }

        if (string.IsNullOrEmpty(request.ClientId))
        {
            return ErrorResult("invalid_request", "client_id is required");
        }

        if (string.IsNullOrEmpty(request.RedirectUri))
        {
            return ErrorResult("invalid_request", "redirect_uri is required");
        }

        if (string.IsNullOrEmpty(request.State))
        {
            return ErrorResult("invalid_request", "state is required");
        }

        // PKCE is required
        if (string.IsNullOrEmpty(request.CodeChallenge))
        {
            return ErrorResult("invalid_request", "code_challenge is required (PKCE)");
        }

        if (request.CodeChallengeMethod != "S256")
        {
            return ErrorResult("invalid_request", "code_challenge_method must be S256");
        }

        // Validate 'me' parameter (user's profile URL)
        if (string.IsNullOrEmpty(request.Me))
        {
            return ErrorResult("invalid_request", "me (profile URL) is required");
        }

        // Check if the profile host is allowed (personal server mode)
        if (!IsProfileHostAllowed(request.Me))
        {
            return ErrorResult("access_denied", 
                "This server is not configured to authenticate users from the requested website.");
        }

        // Discover identity providers from user's profile
        var discoveryResult = await profileDiscovery.DiscoverProfileAsync(request.Me);
        if (!discoveryResult.Success)
        {
            return ErrorResult("invalid_request", discoveryResult.Error ?? "Failed to discover profile");
        }

        if (!discoveryResult.Providers.Any())
        {
            return ErrorResult("invalid_request", "No supported identity providers found on your website");
        }

        // Create pending authentication session
        var sessionId = GenerateSessionId();
        var scopes = ParseScopes(request.Scope);

        var pending = new Data.Entities.PendingAuthenticationEntity
        {
            SessionId = sessionId,
            ClientId = request.ClientId,
            RedirectUri = request.RedirectUri,
            State = request.State,
            CodeChallenge = request.CodeChallenge,
            CodeChallengeMethod = request.CodeChallengeMethod,
            Scopes = string.Join(" ", scopes),
            ProfileUrl = discoveryResult.ProfileUrl,
            ProvidersJson = System.Text.Json.JsonSerializer.Serialize(discoveryResult.Providers),
            CreatedAt = DateTime.UtcNow,
            ExpiresAt = DateTime.UtcNow.AddMinutes(settings.Value.PendingAuthenticationExpirationMinutes)
        };

        dbContext.PendingAuthentications.Add(pending);
        await dbContext.SaveChangesAsync();

        // Determine next step based on number of providers
        if (discoveryResult.Providers.Count == 1)
        {
            // Single provider - auto-redirect to OAuth
            var provider = discoveryResult.Providers[0];
            var identityProvider = providerFactory.GetProviderByType(provider.Type);
            
            if (identityProvider != null)
            {
                // Generate provider state and update pending auth
                var providerState = GenerateSessionId();
                pending.SelectedProviderType = provider.Type;
                pending.ProviderState = providerState;
                await dbContext.SaveChangesAsync();
                
                // Build OAuth redirect URL
                var redirectUri = $"{talosSettings.Value.BaseUrl}/callback/{provider.Type}";
                result.RedirectUrl = identityProvider.GetAuthorizationUrl(providerState, redirectUri);
            }
            else
            {
                // Fallback to selection page if provider not found
                result.RedirectUrl = $"/select-provider?session_id={sessionId}";
            }
        }
        else
        {
            // Multiple providers - show selection UI
            result.RedirectUrl = $"/select-provider?session_id={sessionId}";
        }

        result.Success = true;
        result.SessionId = sessionId;
        return result;
    }

    public async Task<PendingAuthentication?> GetPendingAuthenticationAsync(string sessionId)
    {
        var entity = await dbContext.PendingAuthentications
            .FirstOrDefaultAsync(p => p.SessionId == sessionId && p.ExpiresAt > DateTime.UtcNow);

        if (entity == null)
            return null;

        var providers = string.IsNullOrEmpty(entity.ProvidersJson)
            ? new List<DiscoveredProvider>()
            : System.Text.Json.JsonSerializer.Deserialize<List<DiscoveredProvider>>(entity.ProvidersJson) ?? new();

        return new PendingAuthentication
        {
            SessionId = entity.SessionId,
            ClientId = entity.ClientId,
            RedirectUri = entity.RedirectUri,
            State = entity.State,
            CodeChallenge = entity.CodeChallenge,
            CodeChallengeMethod = entity.CodeChallengeMethod,
            Scopes = entity.Scopes?.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList() ?? new(),
            ProfileUrl = entity.ProfileUrl,
            Providers = providers,
            SelectedProviderType = entity.SelectedProviderType,
            ProviderState = entity.ProviderState,
            IsAuthenticated = entity.IsAuthenticated,
            IsConsentGiven = entity.IsConsentGiven,
            CreatedAt = entity.CreatedAt,
            ExpiresAt = entity.ExpiresAt
        };
    }

    public async Task UpdatePendingAuthenticationAsync(PendingAuthentication authentication)
    {
        var entity = await dbContext.PendingAuthentications
            .FirstOrDefaultAsync(p => p.SessionId == authentication.SessionId);

        if (entity == null)
            return;

        entity.SelectedProviderType = authentication.SelectedProviderType;
        entity.ProviderState = authentication.ProviderState;
        entity.IsAuthenticated = authentication.IsAuthenticated;
        entity.IsConsentGiven = authentication.IsConsentGiven;

        await dbContext.SaveChangesAsync();
    }

    public async Task<AuthorizationCodeResult> CreateAuthorizationCodeAsync(string sessionId)
    {
        var pending = await GetPendingAuthenticationAsync(sessionId);
        if (pending == null)
        {
            return new AuthorizationCodeResult { Error = "Invalid or expired session" };
        }

        if (!pending.IsAuthenticated || !pending.IsConsentGiven)
        {
            return new AuthorizationCodeResult { Error = "Authentication or consent not completed" };
        }

        var code = GenerateAuthorizationCode();

        var codeEntity = new Data.Entities.AuthorizationCodeEntity
        {
            Code = code,
            ClientId = pending.ClientId,
            RedirectUri = pending.RedirectUri,
            ProfileUrl = pending.ProfileUrl,
            Scopes = string.Join(" ", pending.Scopes),
            CodeChallenge = pending.CodeChallenge,
            CodeChallengeMethod = pending.CodeChallengeMethod,
            CreatedAt = DateTime.UtcNow,
            ExpiresAt = DateTime.UtcNow.AddMinutes(settings.Value.AuthorizationCodeExpirationMinutes)
        };

        dbContext.AuthorizationCodes.Add(codeEntity);

        // Remove the pending authentication
        var pendingEntity = await dbContext.PendingAuthentications
            .FirstOrDefaultAsync(p => p.SessionId == sessionId);
        if (pendingEntity != null)
        {
            dbContext.PendingAuthentications.Remove(pendingEntity);
        }

        await dbContext.SaveChangesAsync();

        var issuer = talosSettings.Value.BaseUrl.TrimEnd('/');
        var redirectUrl = $"{pending.RedirectUri}?code={Uri.EscapeDataString(code)}&state={Uri.EscapeDataString(pending.State)}&iss={Uri.EscapeDataString(issuer)}";

        return new AuthorizationCodeResult
        {
            Success = true,
            Code = code,
            RedirectUrl = redirectUrl
        };
    }

    public async Task<AuthorizationCode?> ValidateAuthorizationCodeAsync(
        string code, string clientId, string redirectUri, string codeVerifier)
    {
        var entity = await dbContext.AuthorizationCodes
            .FirstOrDefaultAsync(c => c.Code == code && !c.IsUsed && c.ExpiresAt > DateTime.UtcNow);

        if (entity == null)
        {
            logger.LogWarning("Authorization code not found or expired: {Code}", code);
            return null;
        }

        if (entity.ClientId != clientId)
        {
            logger.LogWarning("Client ID mismatch for code {Code}", code);
            return null;
        }

        if (entity.RedirectUri != redirectUri)
        {
            logger.LogWarning("Redirect URI mismatch for code {Code}", code);
            return null;
        }

        // Validate PKCE
        if (!pkceService.ValidateCodeVerifier(codeVerifier, entity.CodeChallenge!, entity.CodeChallengeMethod!))
        {
            logger.LogWarning("PKCE validation failed for code {Code}", code);
            return null;
        }

        // Mark code as used
        entity.IsUsed = true;
        await dbContext.SaveChangesAsync();

        return new AuthorizationCode
        {
            Code = entity.Code,
            ClientId = entity.ClientId,
            RedirectUri = entity.RedirectUri,
            ProfileUrl = entity.ProfileUrl,
            Scopes = entity.Scopes?.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList() ?? new(),
            CodeChallenge = entity.CodeChallenge,
            CodeChallengeMethod = entity.CodeChallengeMethod,
            CreatedAt = entity.CreatedAt,
            ExpiresAt = entity.ExpiresAt,
            IsUsed = entity.IsUsed
        };
    }

    private static List<string> ParseScopes(string? scope)
    {
        if (string.IsNullOrWhiteSpace(scope))
            return new List<string> { "profile" };

        return scope.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList();
    }

    private static string GenerateSessionId()
    {
        var bytes = new byte[32];
        using var rng = RandomNumberGenerator.Create();
        rng.GetBytes(bytes);
        return Convert.ToBase64String(bytes).Replace("+", "-").Replace("/", "_").TrimEnd('=');
    }

    private static string GenerateAuthorizationCode()
    {
        var bytes = new byte[32];
        using var rng = RandomNumberGenerator.Create();
        rng.GetBytes(bytes);
        return Convert.ToBase64String(bytes).Replace("+", "-").Replace("/", "_").TrimEnd('=');
    }

    private static AuthorizationResult ErrorResult(string error, string description)
    {
        return new AuthorizationResult
        {
            Success = false,
            Error = error,
            ErrorDescription = description
        };
    }

    /// <summary>
    /// Checks if the profile URL's host is allowed based on the AllowedProfileHosts configuration.
    /// Returns true if AllowedProfileHosts is null/empty (all hosts allowed) or if the host matches.
    /// </summary>
    private bool IsProfileHostAllowed(string profileUrl)
    {
        var allowedHosts = talosSettings.Value.AllowedProfileHosts;
        
        // If not configured or empty, allow all hosts
        if (allowedHosts == null || allowedHosts.Length == 0)
            return true;

        // Try to parse the profile URL and extract the host
        if (!Uri.TryCreate(profileUrl, UriKind.Absolute, out var uri))
            return false;

        var host = uri.Host;

        // Case-insensitive exact match against allowed hosts
        return allowedHosts.Any(allowedHost => 
            string.Equals(host, allowedHost, StringComparison.OrdinalIgnoreCase));
    }
}

