# 03 - Authorization Endpoint

## Implementation Status: ✅ COMPLETED (January 31, 2026)

### What Was Implemented
- AuthController with /auth endpoint supporting full IndieAuth authorization flow
- Validation of all required parameters (response_type, client_id, redirect_uri, state, code_challenge, me)
- PKCE requirement enforcement (S256 only)
- Profile discovery integration to find rel="me" links
- PendingAuthentication entity for session state management
- Provider selection API endpoint (/api/auth/providers)
- Provider selection flow (/api/auth/select-provider)
- Consent info retrieval (/api/auth/consent GET)
- Consent submission with authorization code generation (/api/auth/consent POST)
- Error redirect handling with proper OAuth error codes
- Vue.js ProviderSelectView and ConsentView integration

---

## Overview

The authorization endpoint (`/auth`) is where clients redirect users to authenticate and authorize access. With Talos's third-party identity provider model, the flow includes profile discovery and OAuth with providers like GitHub.

## IndieAuth Authorization Flow (Updated for RelMeAuth)

```
┌─────────────┐                              ┌─────────────┐                    ┌─────────────┐
│   Client    │                              │   Talos     │                    │   GitHub    │
│    App      │                              │   /auth     │                    │   OAuth     │
└──────┬──────┘                              └──────┬──────┘                    └──────┬──────┘
       │                                            │                                  │
       │  1. Redirect with auth params + PKCE       │                                  │
       │ ─────────────────────────────────────────► │                                  │
       │                                            │                                  │
       │                    ┌───────────────────────┴───────────────────────┐          │
       │                    │  2. Validate request parameters               │          │
       │                    │  3. Verify client_id (fetch & parse)          │          │
       │                    │  4. Fetch user's "me" URL                     │          │
       │                    │  5. Discover rel="me" links                   │          │
       │                    │  6. Match against supported providers         │          │
       │                    └───────────────────────┬───────────────────────┘          │
       │                                            │                                  │
       │                    ┌───────────────────────┴───────────────────────┐          │
       │                    │  7. If multiple providers: show picker        │          │
       │                    │     If one provider: redirect directly        │          │
       │                    │     If none: show error with instructions     │          │
       │                    └───────────────────────┬───────────────────────┘          │
       │                                            │                                  │
       │                                            │  8. Redirect to GitHub OAuth     │
       │                                            │─────────────────────────────────►│
       │                                            │                                  │
       │                                            │  9. User authenticates           │
       │                                            │◄─────────────────────────────────│
       │                                            │                                  │
       │                    ┌───────────────────────┴───────────────────────┐          │
       │                    │  10. Verify GitHub user matches rel="me"      │          │
       │                    │  11. Check reciprocal link on GitHub profile  │          │
       │                    │  12. Show consent screen (Vue.js)             │          │
       │                    │  13. User approves                            │          │
       │                    │  14. Generate authorization code              │          │
       │                    └───────────────────────┬───────────────────────┘          │
       │                                            │                                  │
       │  15. Redirect to redirect_uri with code    │                                  │
       │ ◄───────────────────────────────────────── │                                  │
       │                                            │                                  │
```

## Request Parameters

### GET /auth

| Parameter | Required | Description |
|-----------|----------|-------------|
| `response_type` | Yes | Must be `code` |
| `client_id` | Yes | URL of the client application |
| `redirect_uri` | Yes | URL to redirect after authorization |
| `state` | Yes | Random string for CSRF protection |
| `code_challenge` | Yes* | PKCE code challenge (base64url encoded) |
| `code_challenge_method` | Yes* | Must be `S256` |
| `scope` | No | Space-separated list of scopes |
| `me` | Yes | User's profile URL to authenticate |

*PKCE is required per the IndieAuth spec

### Example Request

```
GET /auth?response_type=code
    &client_id=https://app.example.com/
    &redirect_uri=https://app.example.com/callback
    &state=1234567890
    &code_challenge=E9Melhoa2OwvFrEMTJguCHaoeK1t8URWbuGJSstw-cM
    &code_challenge_method=S256
    &scope=profile+email
    &me=https://jane.example.com/
```

## Implementation

### AuthController.cs

```csharp
[Route("auth")]
[ApiController]
public class AuthController : ControllerBase
{
    private readonly IAuthorizationService _authService;
    private readonly IClientVerificationService _clientService;
    private readonly IProfileDiscoveryService _discoveryService;
    private readonly ILogger<AuthController> _logger;

    public AuthController(
        IAuthorizationService authService,
        IClientVerificationService clientService,
        IProfileDiscoveryService discoveryService,
        ILogger<AuthController> logger)
    {
        _authService = authService;
        _clientService = clientService;
        _discoveryService = discoveryService;
        _logger = logger;
    }

    /// <summary>
    /// Authorization endpoint - handles initial authorization request
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> Authorize(
        [FromQuery] string response_type,
        [FromQuery] string client_id,
        [FromQuery] string redirect_uri,
        [FromQuery] string state,
        [FromQuery] string code_challenge,
        [FromQuery] string code_challenge_method,
        [FromQuery] string? scope,
        [FromQuery] string me)  // Required - user's profile URL
    {
        // 1. Validate required parameters
        var validationResult = ValidateRequest(
            response_type, client_id, redirect_uri, 
            state, code_challenge, code_challenge_method, me);
        
        if (!validationResult.IsValid)
        {
            return BadRequest(new { error = validationResult.Error });
        }

        // 2. Verify client_id
        var clientInfo = await _clientService.VerifyClientAsync(client_id);
        if (clientInfo == null)
        {
            return BadRequest(new { error = "invalid_client_id" });
        }

        // 3. Validate redirect_uri matches client
        if (!_clientService.ValidateRedirectUri(client_id, redirect_uri))
        {
            return BadRequest(new { error = "invalid_redirect_uri" });
        }

        // 4. Discover identity providers from user's profile
        var discoveryResult = await _discoveryService.DiscoverAsync(me);
        
        if (!discoveryResult.Success)
        {
            // Store error info and redirect to error page
            var errorId = await _authService.StoreErrorAsync(new AuthorizationError
            {
                Error = "discovery_failed",
                ErrorDescription = discoveryResult.Error,
                ClientId = client_id,
                RedirectUri = redirect_uri,
                State = state
            });
            
            return Redirect($"/error?id={errorId}");
        }

        // 5. Store pending authentication request
        var pendingAuthId = await _authService.StorePendingAuthenticationAsync(
            new PendingAuthentication
            {
                ClientId = client_id,
                RedirectUri = redirect_uri,
                State = state,
                CodeChallenge = code_challenge,
                CodeChallengeMethod = code_challenge_method,
                Scope = scope,
                Me = me,
                DiscoveredProviders = discoveryResult.Providers,
                CreatedAt = DateTime.UtcNow
            });

        // 6. Determine next step based on discovered providers
        if (discoveryResult.Providers.Count == 0)
        {
            // No supported providers found - show error
            return Redirect($"/error?id={pendingAuthId}&reason=no_providers");
        }
        else if (discoveryResult.Providers.Count == 1)
        {
            // Single provider - redirect directly to OAuth
            var provider = discoveryResult.Providers[0];
            return Redirect($"/auth/provider/{provider.ProviderType}?pending={pendingAuthId}");
        }
        else
        {
            // Multiple providers - show picker UI
            return Redirect($"/select-provider?pending={pendingAuthId}");
        }
    }

    /// <summary>
    /// Initiates OAuth flow with selected identity provider
    /// </summary>
    [HttpGet("provider/{providerType}")]
    public async Task<IActionResult> StartProviderAuth(
        string providerType,
        [FromQuery] string pending)
    {
        var pendingAuth = await _authService.GetPendingAuthenticationAsync(pending);
        if (pendingAuth == null)
        {
            return BadRequest(new { error = "invalid_pending_auth" });
        }

        // Find the selected provider from discovered list
        var selectedProvider = pendingAuth.DiscoveredProviders
            .FirstOrDefault(p => p.ProviderType == providerType);
        
        if (selectedProvider == null)
        {
            return BadRequest(new { error = "provider_not_found" });
        }

        // Update pending auth with selected provider
        pendingAuth.SelectedProvider = selectedProvider;
        await _authService.UpdatePendingAuthenticationAsync(pendingAuth);

        // Get the identity provider service
        var identityProvider = _providerFactory.GetProvider(providerType);
        if (identityProvider == null)
        {
            return BadRequest(new { error = "unsupported_provider" });
        }

        // Generate state for provider OAuth (different from client state)
        var providerState = await _authService.GenerateProviderStateAsync(pending);
        
        // Build callback URL
        var callbackUrl = $"{Request.Scheme}://{Request.Host}/callback/{providerType}";
        
        // Redirect to provider OAuth
        var authUrl = identityProvider.GetAuthorizationUrl(providerState, callbackUrl);
        return Redirect(authUrl);
    }

    /// <summary>
    /// API endpoint for provider selection page
    /// </summary>
    [HttpGet("pending/{pendingId}")]
    public async Task<IActionResult> GetPendingAuth(string pendingId)
    {
        var pendingAuth = await _authService.GetPendingAuthenticationAsync(pendingId);
        if (pendingAuth == null)
        {
            return NotFound();
        }

        var clientInfo = await _clientService.VerifyClientAsync(pendingAuth.ClientId);

        return Ok(new
        {
            me = pendingAuth.Me,
            clientId = pendingAuth.ClientId,
            clientName = clientInfo?.Name ?? pendingAuth.ClientId,
            clientLogo = clientInfo?.Logo,
            scope = pendingAuth.Scope,
            providers = pendingAuth.DiscoveredProviders.Select(p => new
            {
                providerType = p.ProviderType,
                displayName = p.DisplayName,
                username = p.Username,
                iconUrl = p.IconUrl
            })
        });
    }

    /// <summary>
    /// API endpoint for consent page to approve/deny
    /// </summary>
    [HttpPost("approve")]
    public async Task<IActionResult> ApproveAuthorization([FromBody] ApproveRequest request)
    {
        var pendingAuth = await _authService.GetPendingAuthenticationAsync(request.PendingId);
        if (pendingAuth == null || !pendingAuth.ProviderVerified)
        {
            return NotFound();
        }

        if (!request.Approved)
        {
            // User denied - redirect with error
            var denyUrl = BuildRedirectUrl(pendingAuth.RedirectUri, new Dictionary<string, string>
            {
                ["error"] = "access_denied",
                ["state"] = pendingAuth.State
            });
            return Ok(new { redirectUrl = denyUrl });
        }

        // Generate authorization code
        var code = await _authService.GenerateAuthorizationCodeAsync(pendingAuth);

        // Build redirect URL with code
        var redirectUrl = BuildRedirectUrl(pendingAuth.RedirectUri, new Dictionary<string, string>
        {
            ["code"] = code,
            ["state"] = pendingAuth.State
        });

        // Clean up pending auth
        await _authService.DeletePendingAuthenticationAsync(request.PendingId);

        return Ok(new { redirectUrl });
    }

    private ValidationResult ValidateRequest(
        string responseType,
        string clientId,
        string redirectUri,
        string state,
        string codeChallenge,
        string codeChallengeMethod,
        string me)
    {
        if (responseType != "code")
            return ValidationResult.Fail("unsupported_response_type");
        
        if (string.IsNullOrEmpty(clientId) || !Uri.TryCreate(clientId, UriKind.Absolute, out _))
            return ValidationResult.Fail("invalid_client_id");
        
        if (string.IsNullOrEmpty(redirectUri) || !Uri.TryCreate(redirectUri, UriKind.Absolute, out _))
            return ValidationResult.Fail("invalid_redirect_uri");
        
        if (string.IsNullOrEmpty(state))
            return ValidationResult.Fail("missing_state");
        
        if (string.IsNullOrEmpty(codeChallenge))
            return ValidationResult.Fail("missing_code_challenge");
        
        if (codeChallengeMethod != "S256")
            return ValidationResult.Fail("invalid_code_challenge_method");
        
        if (string.IsNullOrEmpty(me) || !Uri.TryCreate(me, UriKind.Absolute, out _))
            return ValidationResult.Fail("invalid_me");
        
        return ValidationResult.Success();
    }

    private string BuildRedirectUrl(string baseUrl, Dictionary<string, string> parameters)
    {
        var uriBuilder = new UriBuilder(baseUrl);
        var query = HttpUtility.ParseQueryString(uriBuilder.Query);
        
        foreach (var param in parameters)
        {
            query[param.Key] = param.Value;
        }
        
        uriBuilder.Query = query.ToString();
        return uriBuilder.ToString();
    }
}
```

### Callback Controller

Handles OAuth callbacks from identity providers:

```csharp
[Route("callback")]
[ApiController]
public class CallbackController : ControllerBase
{
    private readonly IAuthorizationService _authService;
    private readonly IIdentityProviderFactory _providerFactory;
    private readonly ILogger<CallbackController> _logger;

    public CallbackController(
        IAuthorizationService authService,
        IIdentityProviderFactory providerFactory,
        ILogger<CallbackController> logger)
    {
        _authService = authService;
        _providerFactory = providerFactory;
        _logger = logger;
    }

    /// <summary>
    /// OAuth callback from GitHub
    /// </summary>
    [HttpGet("github")]
    public async Task<IActionResult> GitHubCallback(
        [FromQuery] string code,
        [FromQuery] string state)
    {
        return await HandleProviderCallback("github", code, state);
    }

    private async Task<IActionResult> HandleProviderCallback(
        string providerType,
        string code,
        string state)
    {
        // 1. Validate state and get pending auth
        var pendingId = await _authService.ValidateProviderStateAsync(state);
        if (pendingId == null)
        {
            return Redirect("/error?reason=invalid_state");
        }

        var pendingAuth = await _authService.GetPendingAuthenticationAsync(pendingId);
        if (pendingAuth == null)
        {
            return Redirect("/error?reason=expired");
        }

        // 2. Get the identity provider
        var provider = _providerFactory.GetProvider(providerType);
        if (provider == null)
        {
            return Redirect("/error?reason=unsupported_provider");
        }

        // 3. Exchange code for access token
        var callbackUrl = $"{Request.Scheme}://{Request.Host}/callback/{providerType}";
        var tokenResult = await provider.ExchangeCodeAsync(code, callbackUrl);
        
        if (!tokenResult.Success)
        {
            _logger.LogWarning("Token exchange failed: {Error}", tokenResult.Error);
            return Redirect($"/error?pending={pendingId}&reason=token_exchange_failed");
        }

        // 4. Verify the authenticated user
        var selectedProvider = pendingAuth.SelectedProvider;
        var verifyResult = await provider.VerifyAsync(
            tokenResult.AccessToken!,
            selectedProvider!.Username,
            pendingAuth.Me);

        if (!verifyResult.Success)
        {
            _logger.LogWarning("Verification failed: {Error}", verifyResult.Error);
            return Redirect($"/error?pending={pendingId}&reason=verification_failed");
        }

        // 5. Check reciprocal link (warning if not present, but allow)
        if (!verifyResult.ReciprocaLinkVerified)
        {
            _logger.LogWarning(
                "Reciprocal link not found for {Me} on {Provider}",
                pendingAuth.Me, providerType);
            // Could show warning but still allow
        }

        // 6. Mark pending auth as verified
        pendingAuth.ProviderVerified = true;
        pendingAuth.VerifiedAt = DateTime.UtcNow;
        pendingAuth.VerifiedProvider = providerType;
        pendingAuth.VerifiedUsername = verifyResult.Username;
        await _authService.UpdatePendingAuthenticationAsync(pendingAuth);

        // 7. Redirect to consent page
        return Redirect($"/consent?pending={pendingId}");
    }
}
```

### Models

```csharp
// Models/PendingAuthentication.cs (stored in database)
public class PendingAuthentication
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    
    // Original IndieAuth request
    public string ClientId { get; set; } = "";
    public string RedirectUri { get; set; } = "";
    public string State { get; set; } = "";
    public string CodeChallenge { get; set; } = "";
    public string CodeChallengeMethod { get; set; } = "";
    public string? Scope { get; set; }
    public string Me { get; set; } = "";  // User's profile URL
    
    // Discovery results (serialized JSON)
    public string DiscoveredProvidersJson { get; set; } = "[]";
    
    [NotMapped]
    public List<DiscoveredProvider> DiscoveredProviders
    {
        get => JsonSerializer.Deserialize<List<DiscoveredProvider>>(DiscoveredProvidersJson) ?? new();
        set => DiscoveredProvidersJson = JsonSerializer.Serialize(value);
    }
    
    // Selected provider
    public string? SelectedProviderJson { get; set; }
    
    [NotMapped]
    public DiscoveredProvider? SelectedProvider
    {
        get => SelectedProviderJson != null 
            ? JsonSerializer.Deserialize<DiscoveredProvider>(SelectedProviderJson) 
            : null;
        set => SelectedProviderJson = value != null 
            ? JsonSerializer.Serialize(value) 
            : null;
    }
    
    // Verification status
    public bool ProviderVerified { get; set; }
    public DateTime? VerifiedAt { get; set; }
    public string? VerifiedProvider { get; set; }
    public string? VerifiedUsername { get; set; }
    
    // Timestamps
    public DateTime CreatedAt { get; set; }
    public DateTime ExpiresAt { get; set; }
}

// Models/ApproveRequest.cs
public class ApproveRequest
{
    public string PendingId { get; set; } = "";
    public bool Approved { get; set; }
}
```

## Vue.js Components

### ProviderSelectView.vue

Shows when multiple identity providers are discovered:

```vue
<template>
  <div class="min-h-screen bg-gray-100 flex items-center justify-center p-4">
    <div class="bg-white rounded-lg shadow-lg max-w-md w-full p-6">
      <h1 class="text-xl font-semibold text-gray-900 mb-2">Choose Sign-In Method</h1>
      <p class="text-gray-600 mb-6">
        Select how to verify your identity as 
        <strong class="text-gray-900">{{ me }}</strong>
      </p>

      <div class="space-y-3">
        <button
          v-for="provider in providers"
          :key="provider.providerType"
          @click="selectProvider(provider.providerType)"
          :disabled="loading"
          class="w-full flex items-center gap-4 p-4 border rounded-lg hover:bg-gray-50 transition-colors disabled:opacity-50"
        >
          <img 
            v-if="provider.iconUrl" 
            :src="provider.iconUrl" 
            :alt="provider.displayName"
            class="w-8 h-8"
          >
          <div class="text-left flex-1">
            <p class="font-medium text-gray-900">Continue with {{ provider.displayName }}</p>
            <p class="text-sm text-gray-500">@{{ provider.username }}</p>
          </div>
          <svg class="w-5 h-5 text-gray-400" fill="none" stroke="currentColor" viewBox="0 0 24 24">
            <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M9 5l7 7-7 7"/>
          </svg>
        </button>
      </div>

      <div class="mt-6 pt-6 border-t">
        <p class="text-sm text-gray-500 text-center">
          Signing in to <strong>{{ clientName }}</strong>
        </p>
      </div>
    </div>
  </div>
</template>

<script setup lang="ts">
import { ref, onMounted } from 'vue'
import { useRoute } from 'vue-router'

const route = useRoute()
const pendingId = route.query.pending as string

const loading = ref(false)
const me = ref('')
const clientName = ref('')
const providers = ref<any[]>([])

async function loadPending() {
  const response = await fetch(`/auth/pending/${pendingId}`)
  const data = await response.json()
  me.value = data.me
  clientName.value = data.clientName
  providers.value = data.providers
}

function selectProvider(providerType: string) {
  loading.value = true
  window.location.href = `/auth/provider/${providerType}?pending=${pendingId}`
}

onMounted(loadPending)
</script>
```

### ConsentView.vue

Shows after successful identity provider verification:

```vue
<template>
  <div class="min-h-screen bg-gray-100 flex items-center justify-center p-4">
    <div class="bg-white rounded-lg shadow-lg max-w-md w-full p-6">
      <div class="text-center mb-6">
        <div class="w-16 h-16 bg-green-100 rounded-full flex items-center justify-center mx-auto mb-4">
          <svg class="w-8 h-8 text-green-600" fill="none" stroke="currentColor" viewBox="0 0 24 24">
            <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M5 13l4 4L19 7"/>
          </svg>
        </div>
        <h1 class="text-xl font-semibold text-gray-900">Identity Verified</h1>
        <p class="text-gray-600 mt-1">via {{ verifiedProvider }}</p>
      </div>

      <div class="bg-gray-50 rounded-lg p-4 mb-6">
        <p class="text-sm text-gray-600 mb-1">Signing in as</p>
        <p class="font-medium text-gray-900">{{ me }}</p>
      </div>

      <!-- Client Info -->
      <div class="flex items-center gap-4 mb-6 p-4 border rounded-lg">
        <img 
          v-if="client.logo" 
          :src="client.logo" 
          :alt="client.name"
          class="w-12 h-12 rounded-lg"
        >
        <div>
          <p class="font-medium text-gray-900">{{ client.name }}</p>
          <p class="text-sm text-gray-500">wants to access your identity</p>
        </div>
      </div>

      <!-- Scopes -->
      <div v-if="scopes.length > 0" class="mb-6">
        <p class="text-sm font-medium text-gray-700 mb-2">This will allow the app to:</p>
        <ul class="space-y-2">
          <li v-for="scope in scopes" :key="scope" class="flex items-center gap-2 text-sm">
            <svg class="w-4 h-4 text-green-500" fill="currentColor" viewBox="0 0 20 20">
              <path fill-rule="evenodd" d="M16.707 5.293a1 1 0 010 1.414l-8 8a1 1 0 01-1.414 0l-4-4a1 1 0 011.414-1.414L8 12.586l7.293-7.293a1 1 0 011.414 0z" clip-rule="evenodd"/>
            </svg>
            <span>{{ getScopeDescription(scope) }}</span>
          </li>
        </ul>
      </div>

      <!-- Actions -->
      <div class="flex gap-3">
        <button 
          @click="deny"
          :disabled="submitting"
          class="flex-1 px-4 py-2 border border-gray-300 rounded-lg text-gray-700 hover:bg-gray-50 disabled:opacity-50"
        >
          Deny
        </button>
        <button 
          @click="approve"
          :disabled="submitting"
          class="flex-1 px-4 py-2 bg-indigo-600 text-white rounded-lg hover:bg-indigo-700 disabled:opacity-50"
        >
          {{ submitting ? 'Authorizing...' : 'Authorize' }}
        </button>
      </div>
    </div>
  </div>
</template>

<script setup lang="ts">
import { ref, computed, onMounted } from 'vue'
import { useRoute } from 'vue-router'
import { getAuthRequest, approveAuth } from '@/api/auth'

const route = useRoute()
const requestId = computed(() => route.query.request_id as string)

const loading = ref(true)
const error = ref<string | null>(null)
const submitting = ref(false)
const client = ref<any>({})
const userProfile = ref('')

const scopes = computed(() => {
  if (!client.value.scope) return []
  return client.value.scope.split(' ').filter(Boolean)
})

const scopeDescriptions: Record<string, string> = {
  profile: 'Access your profile information',
  email: 'Access your email address',
  create: 'Create new content on your behalf',
  update: 'Update existing content',
  delete: 'Delete content',
  media: 'Upload media files'
}

function getScopeDescription(scope: string): string {
  return scopeDescriptions[scope] || scope
}

async function loadRequest() {
  try {
    const data = await getAuthRequest(requestId.value)
    client.value = data
    // Get user profile from config or session
    userProfile.value = data.me || 'https://example.com/'
  } catch (e) {
    error.value = 'Failed to load authorization request'
  } finally {
    loading.value = false
  }
}

async function approve() {
  submitting.value = true
  try {
    const result = await approveAuth(requestId.value, true)
    window.location.href = result.redirectUrl
  } catch (e) {
    error.value = 'Failed to authorize'
    submitting.value = false
  }
}

async function deny() {
  submitting.value = true
  try {
    const result = await approveAuth(requestId.value, false)
    window.location.href = result.redirectUrl
  } catch (e) {
    error.value = 'Failed to deny'
    submitting.value = false
  }
}

onMounted(loadRequest)
</script>
```

## Error Responses

| Error Code | Description |
|------------|-------------|
| `invalid_request` | Missing or invalid parameters |
| `unsupported_response_type` | response_type is not "code" |
| `invalid_client_id` | client_id is not a valid URL |
| `invalid_redirect_uri` | redirect_uri doesn't match client |
| `invalid_me` | me parameter is missing or invalid URL |
| `discovery_failed` | Failed to fetch or parse user's profile |
| `no_providers` | No supported identity providers found |
| `verification_failed` | Identity provider verification failed |
| `access_denied` | User denied the authorization |

## Security Considerations

1. **PKCE Required**: Always require `code_challenge` and `code_challenge_method=S256`
2. **State Validation**: Use separate state for client and provider OAuth
3. **Pending Auth Expiration**: Pending authentications expire after 30 minutes
4. **Provider State Binding**: Provider OAuth state tied to pending auth ID
5. **Reciprocal Link Check**: Verify identity provider links back to user's site

## Testing

```bash
# Test authorization request
curl -v "http://localhost:5000/auth?\
response_type=code&\
client_id=https://app.example.com/&\
redirect_uri=https://app.example.com/callback&\
state=random123&\
code_challenge=E9Melhoa2OwvFrEMTJguCHaoeK1t8URWbuGJSstw-cM&\
code_challenge_method=S256&\
scope=profile+email&\
me=https://jane.example.com/"
```

## Next Steps

After implementing the authorization endpoint:
1. Implement GitHub OAuth → [05-authentication.md](./05-authentication.md)
2. Implement token endpoint → [04-token-endpoint.md](./04-token-endpoint.md)
