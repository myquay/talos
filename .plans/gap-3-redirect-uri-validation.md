# Fix Plan: GAP-3 — Add `redirect_uri` validation to authorization request

**Status:** Not Started
**Spec requirement:** DISC-9, AUTH-10, SEC-3 (§4.2.2, §5.2, §10.1)
**Severity:** P0 — Critical

---

## Problem

`AuthorizationService.CreateAuthorizationAsync` does not validate the `redirect_uri` parameter at all. The existing `UrlValidator.IsValidRedirectUri()` method is never called. This means:

1. A malicious client could supply any URL as the `redirect_uri`, enabling open redirect attacks
2. If the `redirect_uri` scheme/host/port differ from the `client_id`, the spec requires the server to verify the redirect URL against the client's published `redirect_uri` list — this isn't done
3. Non-HTTPS redirect URIs, URIs with fragments, or URIs with userinfo are accepted without checks
4. JavaScript/data URIs or other dangerous schemes in `redirect_uri` are not blocked

Additionally, the controller's error handling currently redirects errors *to* the unvalidated `redirect_uri`, which means even the error path is an open redirect vector if the `redirect_uri` is malicious. When `redirect_uri` validation fails, the error must NOT redirect to the `redirect_uri` — it should display an error page directly.

## Spec Requirements

Per IndieAuth §5.2:
> If the URL scheme, host or port of the `redirect_uri` in the request do not match that of the `client_id`, then the authorization endpoint SHOULD verify that the requested `redirect_uri` matches one of the redirect URLs published by the client, and SHOULD block the request from proceeding if not.

Per IndieAuth §10.1:
> If the `redirect_uri` scheme, host or port differ from that of the `client_id`, then the authorization server MUST either verify the redirect URL as described in Redirect URL, or display the redirect URL to the user so they can inspect it manually.

Per IndieAuth §4.2.2:
> Authorization endpoints verifying that a `redirect_uri` is allowed for use by a client MUST look for an exact match of the given `redirect_uri` in the request against the list of `redirect_uri`s after resolving any relative URLs.

## Fix Plan

### Step 1: Add `redirect_uri` basic validation in `AuthorizationService`

In `CreateAuthorizationAsync`, after the existing `string.IsNullOrEmpty(request.RedirectUri)` check, add:

```csharp
// Validate redirect_uri is a valid URL with safe scheme
if (!UrlValidator.IsValidRedirectUri(request.RedirectUri, request.ClientId))
{
    return ErrorResult("invalid_request", 
        "redirect_uri is not valid or does not match client_id");
}
```

This uses the existing `IsValidRedirectUri()` which checks:
- Both URLs are valid HTTPS (or localhost)
- Same host, scheme, and port

### Step 2: Improve `UrlValidator.IsValidRedirectUri` to handle cross-origin case

The current implementation flat-out rejects any `redirect_uri` with a different host. Per the spec, cross-origin redirects are allowed IF the client has published that redirect URI. Since we don't yet fetch client metadata (GAP-4), for now we should:

- **Same origin** (scheme + host + port match): Allow immediately ✅
- **Different origin**: Reject with a clear error message indicating the client needs to register the redirect URI

Add a new method or extend the existing one to return a richer result:

```csharp
public enum RedirectUriValidationResult
{
    Valid,              // Same origin — allowed
    InvalidUrl,         // Not a valid URL at all
    CrossOrigin,        // Different host/scheme/port — needs client metadata verification
    DangerousScheme     // javascript:, data:, etc.
}

public static RedirectUriValidationResult ValidateRedirectUri(string? redirectUri, string? clientId)
```

### Step 3: Add dangerous scheme blocking to `UrlValidator`

Add checks for:
- `javascript:` URIs
- `data:` URIs
- Any scheme other than `http` or `https` (which already happens via `IsValidHttpsUrl`, but make it explicit for `redirect_uri`)

### Step 4: Fix controller error handling for invalid `redirect_uri`

In `AuthController.Authorize`, when the error is related to `redirect_uri` validation (e.g., `invalid_request` with redirect_uri issues), the controller must NOT redirect to the `redirect_uri`. Instead, show the error page directly.

The current code:
```csharp
if (!string.IsNullOrEmpty(redirectUri) && Uri.TryCreate(redirectUri, UriKind.Absolute, out _))
{
    var errorRedirect = $"{redirectUri}?error=...";
    return Redirect(errorRedirect);
}
```

This should be changed so that redirect_uri validation errors bypass the redirect-to-client logic. One approach: add a flag to `AuthorizationResult` (e.g., `RedirectUriInvalid`) that tells the controller to skip client redirect.

### Step 5: Add dot-segment check to redirect URI validation

Per spec §3.3 (Client IDs) and by extension redirect URIs: MUST NOT contain single-dot or double-dot path segments. Add validation:

```csharp
// Check for path traversal segments
var segments = uri.AbsolutePath.Split('/');
if (segments.Any(s => s == "." || s == ".."))
    return false;
```

---

## Unit Tests

### File: `tests/Talos.Web.Tests/Validators/UrlValidatorRedirectUriTests.cs`

```csharp
// === Basic validation ===

[Fact] RedirectUri_SameOrigin_Valid
  // redirect_uri=https://app.example.com/callback, client_id=https://app.example.com/ → valid

[Fact] RedirectUri_SameOriginDifferentPath_Valid
  // redirect_uri=https://app.example.com/auth/callback, client_id=https://app.example.com/ → valid

[Fact] RedirectUri_Null_Invalid
  // redirect_uri=null → invalid

[Fact] RedirectUri_Empty_Invalid
  // redirect_uri="" → invalid

[Fact] RedirectUri_NotAbsoluteUrl_Invalid
  // redirect_uri="/callback" → invalid

// === Scheme checks ===

[Fact] RedirectUri_JavaScriptScheme_Invalid
  // redirect_uri="javascript:alert(1)" → invalid

[Fact] RedirectUri_DataScheme_Invalid
  // redirect_uri="data:text/html,<h1>hi</h1>" → invalid

[Fact] RedirectUri_FtpScheme_Invalid
  // redirect_uri="ftp://app.example.com/callback" → invalid

[Fact] RedirectUri_HttpSchemeNonLocalhost_Invalid
  // redirect_uri="http://app.example.com/callback", client_id=https://app.example.com/ → invalid (diff scheme)

// === Cross-origin checks ===

[Fact] RedirectUri_DifferentHost_Invalid
  // redirect_uri=https://evil.com/callback, client_id=https://app.example.com/ → invalid

[Fact] RedirectUri_DifferentPort_Invalid
  // redirect_uri=https://app.example.com:8443/cb, client_id=https://app.example.com/ → invalid

[Fact] RedirectUri_DifferentScheme_Invalid
  // redirect_uri=http://app.example.com/cb, client_id=https://app.example.com/ → invalid

// === Fragment/userinfo checks ===

[Fact] RedirectUri_WithFragment_Invalid
  // redirect_uri="https://app.example.com/callback#frag" → invalid

[Fact] RedirectUri_WithUserInfo_Invalid
  // redirect_uri="https://user:pass@app.example.com/callback" → invalid

// === Path traversal ===

[Fact] RedirectUri_WithDotSegment_Invalid
  // redirect_uri="https://app.example.com/./callback" → invalid

[Fact] RedirectUri_WithDoubleDotSegment_Invalid
  // redirect_uri="https://app.example.com/../callback" → invalid

// === Localhost ===

[Fact] RedirectUri_LocalhostSameOrigin_Valid
  // redirect_uri=http://localhost:8080/callback, client_id=http://localhost:8080/ → valid

[Fact] RedirectUri_127001SameOrigin_Valid
  // redirect_uri=http://127.0.0.1/callback, client_id=http://127.0.0.1/ → valid
```

### File: `tests/Talos.Web.Tests/Services/AuthorizationServiceRedirectUriTests.cs`

```csharp
// === Service-level integration tests ===

[Fact] CreateAuthorizationAsync_InvalidRedirectUri_ReturnsError
  // redirect_uri="javascript:alert(1)" → error="invalid_request"

[Fact] CreateAuthorizationAsync_CrossOriginRedirectUri_ReturnsError
  // redirect_uri=https://evil.com/callback, client_id=https://app.example.com/ → error

[Fact] CreateAuthorizationAsync_ValidSameOriginRedirectUri_Succeeds
  // redirect_uri=https://app.example.com/callback, client_id=https://app.example.com/ → success

[Fact] CreateAuthorizationAsync_RedirectUriValidationError_DoesNotLeakViaRedirect
  // When redirect_uri is invalid, AuthorizationResult should flag that errors must not
  // redirect to the redirect_uri (controller test or result flag test)
```

---

## Files Modified

| File | Change |
|------|--------|
| `src/Talos.Web/Services/UrlValidator.cs` | Add dot-segment check, dangerous scheme check, possibly `ValidateRedirectUri` enum method |
| `src/Talos.Web/Services/AuthorizationService.cs` | Call `UrlValidator.IsValidRedirectUri()` in `CreateAuthorizationAsync` |
| `src/Talos.Web/Services/IAuthorizationService.cs` | Possibly add `RedirectUriInvalid` flag to `AuthorizationResult` |
| `src/Talos.Web/Controllers/AuthController.cs` | Don't redirect to `redirect_uri` when it failed validation |
| `tests/.../Validators/UrlValidatorRedirectUriTests.cs` | New test file — ~15 tests |
| `tests/.../Services/AuthorizationServiceRedirectUriTests.cs` | New test file — ~4 tests |

## Dependencies

- This fix does NOT require GAP-4 (client_id fetching). Cross-origin redirect URIs are simply rejected until client metadata discovery is implemented.
- Once GAP-4 is done, the cross-origin path can be relaxed to allow redirect URIs that match the client's published list.

## Out of Scope

- Fetching client metadata to verify cross-origin redirect URIs (GAP-4 / Plan item 2.3)
- Custom scheme redirect URIs for native apps (not currently supported; can be added later with client metadata)
