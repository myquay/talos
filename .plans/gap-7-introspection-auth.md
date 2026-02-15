# Fix Plan: GAP-7 — Require authorization on introspection endpoint

**Status:** ✅ Complete
**Spec requirement:** INTRO-2, INTRO-3 (§6.1)
**Severity:** P0 — Critical
**Completed:** All production code and tests implemented. 184/184 tests passing.

---

## Problem

The introspection endpoint at `/token/introspect` currently accepts any POST request with no authentication. Per IndieAuth §6.1 and RFC 7662 §2.1, the introspection endpoint **MUST** require some form of authorization and **MUST** respond with HTTP 401 if authorization is insufficient.

In [TokenController.cs](../src/Talos.Web/Controllers/TokenController.cs#L216), the `Introspect` action:

```csharp
[HttpPost("introspect")]
[Consumes("application/x-www-form-urlencoded")]
public async Task<IActionResult> Introspect([FromForm] string token)
{
    // No authentication check — anyone can query token status
    ...
}
```

This is a security vulnerability: any unauthenticated party can probe whether tokens are valid and extract metadata (profile URL, client ID, scopes, expiry) by simply posting token strings to the endpoint.

## Spec Requirements

Per IndieAuth §6.1:
> The token endpoint [...] MUST require some form of authorization to access the introspection endpoint, as the response may contain sensitive information about the token and its holder.
>
> If the token endpoint does not recognize the authorization provided, the endpoint MUST respond with an HTTP 401 status code.

Per RFC 7662 §2.1:
> To prevent token scanning attacks, the [introspection] endpoint MUST require some form of authorization to access this endpoint.

The spec is deliberately flexible about *what* form of authorization is used. Common approaches:

1. **Bearer token** — resource servers present their own access token (or a dedicated introspection token)
2. **Shared secret / API key** — a pre-configured token checked via header
3. **HTTP Basic Auth** — client credentials

## Design Decision

For Talos (a personal IndieAuth server), the most practical approach is a **configurable bearer token** (shared secret):

- **Why not a full access token?** Talos is a single-user authorization server. The resource server that calls introspection isn't a Talos client with its own OAuth flow — it's typically a Micropub/Microsub endpoint that needs to verify tokens it received.
- **Why bearer token?** It's the standard `Authorization: Bearer <token>` pattern, aligns with how resource servers already send tokens, and is simple to configure.
- **Why configurable?** The operator sets an `IntrospectionSecret` in config. Resource servers include this secret as a Bearer token when calling introspect.

If no `IntrospectionSecret` is configured, the endpoint should remain **protected** (return 401 for all requests) rather than silently falling back to unauthenticated access. This fail-closed approach prevents accidental exposure.

---

## Fix Plan

### Step 1: Add `IntrospectionSecret` to configuration

**File:** `src/Talos.Web/Configuration/IndieAuthSettings.cs`

```csharp
public class IndieAuthSettings
{
    public int AuthorizationCodeExpirationMinutes { get; set; } = 10;
    public int RefreshTokenExpirationDays { get; set; } = 30;
    public int PendingAuthenticationExpirationMinutes { get; set; } = 30;

    /// <summary>
    /// Shared secret that resource servers must present as a Bearer token
    /// when calling the introspection endpoint. MUST be configured for
    /// introspection to function. If null/empty, all introspection requests
    /// are rejected with 401.
    /// </summary>
    public string? IntrospectionSecret { get; set; }
}
```

### Step 2: Add bearer token validation in the `Introspect` action

**File:** `src/Talos.Web/Controllers/TokenController.cs`

The controller needs access to `IndieAuthSettings` (which it already has via `IOptions<IndieAuthSettings> settings`). Add authorization validation at the top of the `Introspect` method:

```csharp
[HttpPost("introspect")]
[Consumes("application/x-www-form-urlencoded")]
public async Task<IActionResult> Introspect([FromForm] string token)
{
    // Per IndieAuth §6.1 — introspection MUST require authorization
    if (!IsIntrospectionAuthorized())
    {
        return Unauthorized(new { error = "unauthorized", error_description = "Bearer token required" });
    }

    // ... existing logic unchanged ...
}
```

Add a private helper:

```csharp
/// <summary>
/// Validates the Authorization header against the configured introspection secret.
/// Per IndieAuth §6.1 and RFC 7662, introspection MUST require authorization.
/// </summary>
private bool IsIntrospectionAuthorized()
{
    var secret = settings.Value.IntrospectionSecret;

    // Fail closed: if no secret is configured, reject all requests
    if (string.IsNullOrEmpty(secret))
        return false;

    var authHeader = Request.Headers.Authorization.ToString();
    if (string.IsNullOrEmpty(authHeader))
        return false;

    // Expect "Bearer <secret>"
    if (!authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        return false;

    var provided = authHeader["Bearer ".Length..].Trim();

    // Constant-time comparison to prevent timing attacks
    return CryptographicOperations.FixedTimeEquals(
        System.Text.Encoding.UTF8.GetBytes(provided),
        System.Text.Encoding.UTF8.GetBytes(secret));
}
```

Key design decisions:
- **Constant-time comparison** via `CryptographicOperations.FixedTimeEquals` prevents timing side-channel attacks on the secret.
- **Fail closed** — no configured secret = 401 for all requests.
- **Standard Bearer scheme** — `Authorization: Bearer <secret>`.

### Step 3: Update the example configuration

**File:** `src/Talos.Web/appsettings.example.json`

Add the new setting under the `IndieAuth` section:

```json
"IndieAuth": {
    "AuthorizationCodeExpirationMinutes": 10,
    "RefreshTokenExpirationDays": 30,
    "PendingAuthenticationExpirationMinutes": 30,
    "IntrospectionSecret": "generate-a-secure-random-string-here"
}
```

### Step 4: Update metadata endpoint (related: GAP-20)

**File:** `src/Talos.Web/Controllers/MetadataController.cs`

Add `introspection_endpoint_auth_methods_supported` to the metadata response:

```csharp
introspection_endpoint_auth_methods_supported = new[] { "Bearer" }
```

This tells clients what authentication method the introspection endpoint expects. This partially addresses GAP-20.

---

## Unit Tests

### File: `tests/Talos.Web.Tests/Controllers/TokenControllerIntrospectionAuthTests.cs`

These tests verify the authorization requirement on the introspection endpoint. Since the controller reads `Request.Headers.Authorization`, tests must set up `HttpContext` with appropriate headers.

```csharp
// ===== No Authorization header =====

[Fact] Introspect_NoAuthorizationHeader_Returns401
  // Setup: create controller with configured IntrospectionSecret
  // Act: call Introspect("some-token") with no Authorization header on HttpContext
  // Assert: UnauthorizedObjectResult (401)

[Fact] Introspect_NoAuthorizationHeader_DoesNotLeakTokenInfo
  // Same as above, but also verify the response body does NOT contain
  // any token metadata (active, me, client_id, etc.)
  // Assert: response contains {error: "unauthorized"}

// ===== Wrong credentials =====

[Fact] Introspect_WrongBearerToken_Returns401
  // Setup: IntrospectionSecret = "correct-secret"
  // Act: set Authorization header to "Bearer wrong-secret"
  // Assert: UnauthorizedObjectResult (401)

[Fact] Introspect_BasicAuthInsteadOfBearer_Returns401
  // Act: set Authorization header to "Basic dXNlcjpwYXNz" (user:pass base64)
  // Assert: UnauthorizedObjectResult (401) — only Bearer is accepted

[Fact] Introspect_EmptyBearerToken_Returns401
  // Act: set Authorization header to "Bearer "
  // Assert: UnauthorizedObjectResult (401)

// ===== No secret configured (fail-closed) =====

[Fact] Introspect_NoSecretConfigured_Returns401
  // Setup: IndieAuthSettings with IntrospectionSecret = null
  // Act: call Introspect with any Authorization header
  // Assert: UnauthorizedObjectResult (401) — fail closed

[Fact] Introspect_EmptySecretConfigured_Returns401
  // Setup: IndieAuthSettings with IntrospectionSecret = ""
  // Act: call Introspect with Authorization: Bearer anything
  // Assert: UnauthorizedObjectResult (401) — fail closed

// ===== Valid credentials =====

[Fact] Introspect_ValidBearerToken_ReturnsOk
  // Setup: IntrospectionSecret = "test-secret-123"
  //   mock tokenService.ValidateAccessTokenAsync returns valid result
  // Act: set Authorization header to "Bearer test-secret-123", call Introspect("valid-token")
  // Assert: OkObjectResult with IntrospectionResponse { Active = true }

[Fact] Introspect_ValidBearerToken_InvalidToken_ReturnsInactive
  // Setup: IntrospectionSecret = "test-secret-123"
  //   mock tokenService.ValidateAccessTokenAsync returns IsValid = false
  // Act: set Authorization header to "Bearer test-secret-123", call Introspect("invalid-token")
  // Assert: OkObjectResult with IntrospectionResponse { Active = false }

[Fact] Introspect_ValidBearerToken_EmptyToken_ReturnsInactive
  // Setup: valid Bearer token in header
  // Act: call Introspect("") with valid auth
  // Assert: OkObjectResult with IntrospectionResponse { Active = false }
```

### Helper pattern for setting up HttpContext in tests

Since `Request.Headers` is read from `HttpContext`, the test helper must attach a default context:

```csharp
private TokenController CreateController(string? introspectionSecret, string? authorizationHeader = null)
{
    var mockSettings = new Mock<IOptions<IndieAuthSettings>>();
    mockSettings.Setup(x => x.Value).Returns(new IndieAuthSettings
    {
        IntrospectionSecret = introspectionSecret,
        AuthorizationCodeExpirationMinutes = 10,
        RefreshTokenExpirationDays = 30
    });

    var controller = new TokenController(
        _mockAuthService.Object,
        _mockTokenService.Object,
        _dbContext,
        mockSettings.Object);

    // Set up HttpContext so Request.Headers is available
    var httpContext = new DefaultHttpContext();
    if (authorizationHeader != null)
    {
        httpContext.Request.Headers.Authorization = authorizationHeader;
    }
    controller.ControllerContext = new ControllerContext
    {
        HttpContext = httpContext
    };

    return controller;
}
```

### Existing test file updates

**File:** `tests/Talos.Web.Tests/Controllers/TokenControllerIntrospectionTests.cs`

The 3 existing introspection tests must be updated to include a valid Authorization header, since they'll now fail with 401 without one. The `CreateController()` helper needs to set up HttpContext with `Authorization: Bearer <test-secret>` and configure `IntrospectionSecret` in settings.

### File: `tests/Talos.Web.Tests/Services/TokenEndpointScopeTests.cs`

The `CreateController()` helper in this file doesn't need changes because those tests don't exercise the introspection endpoint — they test `Exchange()` and `VerifyAuthorizationCode()`, which don't require introspection auth.

---

## Files Modified

| File | Change |
|------|--------|
| `src/Talos.Web/Configuration/IndieAuthSettings.cs` | Add `IntrospectionSecret` property |
| `src/Talos.Web/Controllers/TokenController.cs` | Add `IsIntrospectionAuthorized()` helper and auth check at top of `Introspect` action; add `using System.Security.Cryptography` |
| `src/Talos.Web/Controllers/MetadataController.cs` | Add `introspection_endpoint_auth_methods_supported` to metadata |
| `src/Talos.Web/appsettings.example.json` | Add `IntrospectionSecret` to IndieAuth section |
| `tests/.../Controllers/TokenControllerIntrospectionAuthTests.cs` | New test file — ~10 tests for auth enforcement |
| `tests/.../Controllers/TokenControllerIntrospectionTests.cs` | Update existing 3 tests to include valid auth header |

## Dependencies

- None. This fix is self-contained.

## Security Considerations

- **Constant-time comparison** prevents timing attacks on the introspection secret.
- **Fail-closed design** ensures that misconfiguration (missing secret) doesn't silently expose the endpoint.
- **No rate-limiting change needed** — the existing `token` rate limit policy already covers `/token/introspect`.
- The `IntrospectionSecret` should be generated with at least 32 bytes of cryptographic randomness (e.g., `openssl rand -base64 32`).

## Out of Scope

- JWT-based introspection authentication (overkill for a personal server)
- Mutual TLS authentication
- Full OAuth 2.0 client credentials grant for resource server authentication
- Revocation endpoint authentication (currently unauthenticated; RFC 7009 says this is acceptable since revocation of an unknown token is a no-op)
