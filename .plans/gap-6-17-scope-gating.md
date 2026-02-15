# Fix Plan: GAP-6 + GAP-17 — Enforce no-token-for-empty-scope

**Status:** ✅ Complete
**Spec requirement:** REDEEM-5, AUTH-6 (§5.3.2, §5.3.3)
**Severity:** P0 — Critical
**Completed:** All production code and tests implemented. 173/173 tests passing.

---

## Problem

Two related issues combine to violate the IndieAuth spec's scope/token semantics:

### GAP-17 — `ParseScopes` adds a default `profile` scope when none is provided

In [AuthorizationService.cs](../src/Talos.Web/Services/AuthorizationService.cs#L308):

```csharp
private static List<string> ParseScopes(string? scope)
{
    if (string.IsNullOrWhiteSpace(scope))
        return new List<string> { "profile" };  // ← BUG: invents a scope

    return scope.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList();
}
```

When a client makes an authorization request with no `scope` parameter (or an empty one), this method silently adds `"profile"` as the scope. Per the IndieAuth spec, the distinction between "no scope" and "scope=profile" is critical:

- **No scope requested** → authentication-only flow → client exchanges the code at the **authorization endpoint** and receives only `me` (the profile URL). **No access token is issued.**
- **`scope=profile` requested** → the client can exchange the code at the **token endpoint** and receive an access token with the profile scope.

By adding a default scope, the server effectively upgrades every authentication-only request into a token-eligible request, which violates the spec's intent.

### GAP-6 — Token endpoint issues access tokens even when auth code has no scopes

In [TokenController.cs](../src/Talos.Web/Controllers/TokenController.cs#L52) `HandleAuthorizationCodeGrant`, after validating the auth code, the method unconditionally generates an access token and refresh token — regardless of whether the code was issued with any scopes:

```csharp
var accessToken = tokenService.GenerateAccessToken(
    authCode.ProfileUrl,
    authCode.ClientId,
    authCode.Scopes);
```

Per IndieAuth spec §5.3.3 (REDEEM-5):

> If the authorization code was issued with no `scope`, the token endpoint MUST NOT issue an access token. [...] If the client only needs to know the user who logged in, the client should exchange the authorization code at the authorization endpoint.

Currently, because of GAP-17, this code path is never reached with an empty scope list. But if GAP-17 is fixed alone (removing the default scope), then this method would start issuing access tokens with empty scope strings — still a spec violation.

Both gaps must be fixed together for correctness.

## Spec Requirements

Per IndieAuth §5.3.2 (Authorization Code Verification at Auth Endpoint):
> If the client only needs the user's profile URL (no scope was requested), the client exchanges the code at the **authorization endpoint**. The response MUST include `me`.

Per IndieAuth §5.3.3 (Token Endpoint Code Exchange):
> If the authorization code was issued with no `scope`, the token endpoint **MUST NOT** issue an access token. The client should have exchanged the code at the authorization endpoint instead.

Per IndieAuth §5.2 (Authorization Request):
> `scope` is OPTIONAL. If omitted, the authorization server should treat the request as an authentication-only request (profile URL response only).

---

## Fix Plan

### Step 1: Fix `ParseScopes` to return an empty list for empty/null scope

**File:** `src/Talos.Web/Services/AuthorizationService.cs`

Change:
```csharp
private static List<string> ParseScopes(string? scope)
{
    if (string.IsNullOrWhiteSpace(scope))
        return new List<string> { "profile" };

    return scope.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList();
}
```

To:
```csharp
private static List<string> ParseScopes(string? scope)
{
    if (string.IsNullOrWhiteSpace(scope))
        return new List<string>();

    return scope.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList();
}
```

**Impact:** After this change, when a client sends `scope=` or omits `scope`, the `PendingAuthenticationEntity.Scopes` and subsequent `AuthorizationCodeEntity.Scopes` will store an empty string (`""`), and the `AuthorizationCode.Scopes` list will be empty.

### Step 2: Reject token issuance for scope-less auth codes at the token endpoint

**File:** `src/Talos.Web/Controllers/TokenController.cs`

In `HandleAuthorizationCodeGrant`, after the `ValidateAuthorizationCodeAsync` call and null check, add a scope check:

```csharp
var authCode = await authorizationService.ValidateAuthorizationCodeAsync(
    request.Code, request.ClientId, request.RedirectUri, request.CodeVerifier);

if (authCode == null)
{
    return BadRequest(new TokenErrorResponse
    {
        Error = "invalid_grant",
        ErrorDescription = "Invalid, expired, or already used authorization code"
    });
}

// NEW: Per IndieAuth §5.3.3 — codes with no scope cannot be exchanged for an access token
if (authCode.Scopes == null || authCode.Scopes.Count == 0)
{
    return BadRequest(new TokenErrorResponse
    {
        Error = "invalid_grant",
        ErrorDescription = "This authorization code was issued with no scope and cannot be exchanged for an access token. Use the authorization endpoint instead."
    });
}
```

**Rationale:** The spec says MUST NOT issue a token. Using `invalid_grant` as the error code is appropriate since the auth code is technically valid but cannot be redeemed at this endpoint.

### Step 3: Verify auth endpoint POST still works for scope-less codes

**File:** `src/Talos.Web/Controllers/AuthController.cs`

The existing `VerifyAuthorizationCode` method (auth endpoint POST) already returns only `me`:
```csharp
return Ok(new { me = authCode.ProfileUrl });
```

This is correct for the authentication-only flow. No code changes needed here — only tests to confirm this path works with scope-less auth codes.

### Step 4: Make `ParseScopes` accessible for unit testing (optional)

`ParseScopes` is currently `private static`. Two options:

1. **Keep private, test indirectly** — test via `CreateAuthorizationAsync` by checking the stored `PendingAuthenticationEntity.Scopes` in the DB.
2. **Make `internal static` + `InternalsVisibleTo`** — allows direct unit testing of the scope parsing logic.

Recommendation: Option 1 (test indirectly via service) is sufficient and avoids exposing internals.

---

## Unit Tests

### File: `tests/Talos.Web.Tests/Services/AuthorizationServiceScopeTests.cs`

Tests that `ParseScopes` behavior is correct, verified through the service layer:

```csharp
// === Scope parsing via CreateAuthorizationAsync ===

[Fact] CreateAuthorizationAsync_NoScope_StoresEmptyScopes
  // scope=null → PendingAuth.Scopes should be empty list
  // Verify by reading back the PendingAuthentication from the service
  // After calling CreateAuthorizationAsync with no scope, call GetPendingAuthenticationAsync
  // and assert pending.Scopes is empty

[Fact] CreateAuthorizationAsync_EmptyScope_StoresEmptyScopes
  // scope="" → same as null, PendingAuth.Scopes should be empty list

[Fact] CreateAuthorizationAsync_WhitespaceScope_StoresEmptyScopes
  // scope="  " → same as null/empty

[Fact] CreateAuthorizationAsync_SingleScope_StoresCorrectly
  // scope="profile" → PendingAuth.Scopes == ["profile"]

[Fact] CreateAuthorizationAsync_MultipleScopes_StoresCorrectly
  // scope="profile email create" → PendingAuth.Scopes == ["profile", "email", "create"]

[Fact] CreateAuthorizationAsync_ScopeWithExtraSpaces_NormalizesCorrectly
  // scope="  profile   email  " → PendingAuth.Scopes == ["profile", "email"]
```

### File: `tests/Talos.Web.Tests/Services/TokenEndpointScopeTests.cs`

Tests that the token endpoint rejects scope-less auth codes. These tests need to exercise `HandleAuthorizationCodeGrant` via the controller, so they need to mock `IAuthorizationService` and `ITokenService`:

```csharp
// === Token endpoint scope gating ===

[Fact] Exchange_AuthCodeWithNoScope_ReturnsBadRequest
  // Setup: mock authorizationService.ValidateAuthorizationCodeAsync returns an AuthorizationCode
  //   with Scopes = empty list
  // Act: POST /token with grant_type=authorization_code
  // Assert: 400 Bad Request, error="invalid_grant", description mentions "no scope"

[Fact] Exchange_AuthCodeWithScopes_ReturnsToken
  // Setup: mock returns AuthorizationCode with Scopes = ["profile"]
  // Act: POST /token with grant_type=authorization_code
  // Assert: 200 OK with access_token, refresh_token, me, scope

[Fact] Exchange_AuthCodeWithNullScopes_ReturnsBadRequest
  // Edge case: Scopes is null (shouldn't happen, but defensive)
  // Assert: 400 Bad Request

// === Auth endpoint still works for scope-less codes ===

[Fact] VerifyAuthorizationCode_AuthCodeWithNoScope_ReturnsMe
  // Setup: mock returns AuthorizationCode with Scopes = empty
  // Act: POST /auth with grant_type=authorization_code
  // Assert: 200 OK with { me: "https://example.com/" } — no token fields

[Fact] VerifyAuthorizationCode_AuthCodeWithScope_StillReturnsOnlyMe
  // Auth endpoint POST ALWAYS returns only me, even if code had scopes
  // (Tokens come from the token endpoint, not the auth endpoint)
  // Assert: 200 OK with { me: "..." } — no access_token
```

---

## Files Modified

| File | Change |
|------|--------|
| `src/Talos.Web/Services/AuthorizationService.cs` | Fix `ParseScopes` to return empty list for null/empty scope |
| `src/Talos.Web/Controllers/TokenController.cs` | Add scope check before token issuance — reject empty-scope codes |
| `tests/.../Services/AuthorizationServiceScopeTests.cs` | New test file — ~6 tests for scope parsing |
| `tests/.../Services/TokenEndpointScopeTests.cs` | New test file — ~5 tests for token endpoint scope gating |

## Dependencies

- None. This fix is self-contained.

## Out of Scope

- Downscoping on refresh token requests (§5.5.1 allows clients to request a subset of original scopes — not currently supported, tracked separately)
- Scope validation against an allowed-scopes list (currently all scope strings are accepted as-is)
