# Fix Plan: GAP-2 — Add `client_id` URL validation to authorization flow

**Status:** Complete ✅
**Spec requirement:** ID-9, ID-10 (§3.3)
**Severity:** P1 — High (Medium spec gap + security surface)
**Related gaps:** None (standalone)
**Completed:** 2026-02-15
**Tests:** ~12 expanded validator tests + 7 service-level integration tests, all passing (204/204 total suite)

---

## Problem

`AuthorizationService.CreateAuthorizationAsync` checks that `client_id` is non-empty but never calls `UrlValidator.IsValidClientId()` to validate the URL against the IndieAuth spec. A malformed or malicious `client_id` (e.g. an IP address, a URL with dot-segments, or a non-HTTPS URL) is accepted without error and stored in the pending authentication session.

The `IsValidClientId()` method already exists and is called internally by `IsValidRedirectUri()`, but it is not called directly on the `client_id` parameter itself before the redirect_uri check. This means:

1. An invalid `client_id` paired with a **same-origin** redirect_uri would be caught indirectly by `IsValidRedirectUri` (which calls `IsValidClientId` on its `clientId` parameter), but the error message would blame the redirect_uri rather than the client_id.
2. The error is confusing — the user/developer sees "redirect_uri is not valid" when the real problem is the client_id.
3. The `IsValidClientId()` method itself is incomplete — it does not check for dot-segments per spec §3.3.

## Spec Requirements (§3.3)

> Client identifier URLs:
> - MUST have either an `https` or `http` scheme
> - MUST contain a path component (at minimum `/`)
> - MUST NOT contain single-dot or double-dot path segments
> - MUST NOT contain a fragment component
> - MUST NOT contain a username or password component
> - MAY contain a port
> - Host component MUST be a domain name or loopback IP (`127.0.0.1` or `[::1]`)
> - MUST NOT be any other IP address

---

## Changes

### 1. Enhance `UrlValidator.IsValidClientId()` — add dot-segment check

**File:** `src/Talos.Web/Services/UrlValidator.cs`

The existing method checks scheme (via `IsValidHttpsUrl`), IP addresses, and path presence. It is missing the dot-segment check that is already implemented as `HasDotSegments()` for redirect_uri validation.

**Add** a `HasDotSegments()` call inside `IsValidClientId()`:

```csharp
public static bool IsValidClientId(string? clientId)
{
    if (!IsValidHttpsUrl(clientId))
        return false;

    var uri = new Uri(clientId!);

    // Must not be an IP address (except loopback)
    if (uri.HostNameType == UriHostNameType.IPv4 || uri.HostNameType == UriHostNameType.IPv6)
    {
        if (uri.Host != "127.0.0.1" && uri.Host != "::1" && uri.Host != "localhost")
            return false;
    }

    // Path must be non-empty (at least /)
    if (string.IsNullOrEmpty(uri.AbsolutePath) || uri.AbsolutePath == "")
        return false;

    // Must not contain dot-segments (§3.3)
    if (HasDotSegments(clientId!))
        return false;

    return true;
}
```

This is a single addition (`HasDotSegments` call) at the end of the method, before `return true`.

### 2. Add `client_id` validation call in `CreateAuthorizationAsync`

**File:** `src/Talos.Web/Services/AuthorizationService.cs`

Insert a validation check **after** the null/empty check for `client_id` and **before** the redirect_uri validation. If the client_id is structurally invalid, the redirect_uri is untrusted (since we can't verify origin match).

```csharp
if (string.IsNullOrEmpty(request.ClientId))
{
    return ErrorResult("invalid_request", "client_id is required");
}

// NEW: Validate client_id URL per IndieAuth spec §3.3
if (!UrlValidator.IsValidClientId(request.ClientId))
{
    return ErrorResult("invalid_request",
        "client_id is not a valid URL per the IndieAuth specification",
        redirectUriUntrusted: true);
}

if (string.IsNullOrEmpty(request.RedirectUri))
{
    return ErrorResult("invalid_request", "redirect_uri is required", redirectUriUntrusted: true);
}
```

**Why `redirectUriUntrusted: true`?** If the client_id is invalid, we cannot verify that the redirect_uri belongs to the legitimate client. Redirecting errors to an unverified redirect_uri could enable open redirect attacks. The controller will display the error page directly instead of redirecting.

### 3. Expand `UrlValidator` unit tests

**File:** `tests/Talos.Web.Tests/Validators/UrlValidatorTests.cs`

Add test cases to the existing `IsValidClientId` test section for comprehensive coverage of spec §3.3:

#### Valid client_id URLs (should return `true`)
| Input | Rationale |
|-------|-----------|
| `https://app.example.com/` | Basic valid client_id |
| `https://app.example.com/client` | Path without trailing slash |
| `https://app.example.com/path/to/app` | Deep path |
| `https://app.example.com:8443/` | Port is explicitly allowed by spec |
| `https://app.example.com/?key=value` | Query strings are allowed (not prohibited) |
| `http://localhost/` | Loopback allowed with HTTP |
| `http://localhost:8080/` | Loopback with port |
| `http://127.0.0.1/` | IPv4 loopback allowed |
| `http://[::1]/` | IPv6 loopback allowed |

#### Invalid client_id URLs (should return `false`)
| Input | Rationale |
|-------|-----------|
| `http://app.example.com/` | HTTP not allowed for non-loopback (stricter than spec) |
| `https://192.168.1.1/` | Non-loopback IPv4 |
| `https://10.0.0.1/` | Private IPv4 |
| `https://[2001:db8::1]/` | Non-loopback IPv6 |
| `https://app.example.com/foo/../bar` | Dot-segments not allowed |
| `https://app.example.com/./path` | Single-dot segment not allowed |
| `https://app.example.com/#fragment` | Fragment not allowed (checked by `IsValidHttpsUrl`) |
| `https://user:pass@app.example.com/` | Userinfo not allowed (checked by `IsValidHttpsUrl`) |
| `ftp://app.example.com/` | Wrong scheme |
| `""` | Empty string |
| `null` | Null |
| `not-a-url` | Not a URL at all |

### 4. Add service-level integration tests

**File:** `tests/Talos.Web.Tests/Services/AuthorizationServiceClientIdTests.cs` (new)

Follow the same pattern as `AuthorizationServiceRedirectUriTests.cs`:

- In-memory `TalosDbContext`
- Mocked `IProfileDiscoveryService`, `IPkceService`, `IIdentityProviderFactory`, `ILogger`
- Helper `CreateService()` and `CreateRequest()`

#### Test cases

| Test | Input | Expected |
|------|-------|----------|
| `CreateAuthorizationAsync_InvalidClientIdIpAddress_ReturnsError` | `client_id=https://192.168.1.1/` | `Success=false`, `Error=invalid_request`, `RedirectUriUntrusted=true` |
| `CreateAuthorizationAsync_InvalidClientIdDotSegments_ReturnsError` | `client_id=https://app.example.com/foo/../bar` | `Success=false`, `Error=invalid_request`, `RedirectUriUntrusted=true` |
| `CreateAuthorizationAsync_InvalidClientIdFragment_ReturnsError` | `client_id=https://app.example.com/#frag` | `Success=false`, `Error=invalid_request`, `RedirectUriUntrusted=true` |
| `CreateAuthorizationAsync_InvalidClientIdHttpNonLocalhost_ReturnsError` | `client_id=http://app.example.com/` | `Success=false`, `Error=invalid_request`, `RedirectUriUntrusted=true` |
| `CreateAuthorizationAsync_ValidClientIdHttps_Proceeds` | `client_id=https://app.example.com/` | No client_id error (may fail later on profile discovery, which is fine — demonstrates client_id check passed) |
| `CreateAuthorizationAsync_ValidClientIdLocalhost_Proceeds` | `client_id=http://localhost:8080/` | No client_id error |
| `CreateAuthorizationAsync_InvalidClientId_DoesNotReachProfileDiscovery` | `client_id=https://10.0.0.1/` | Profile discovery mock never called |

---

## Verification

1. Run existing tests — ensure no regressions (especially `UrlValidatorTests`, `UrlValidatorRedirectUriTests`, and `AuthorizationServiceRedirectUriTests` which indirectly exercise `IsValidClientId`)
2. Run new validator tests — all pass
3. Run new service tests — all pass
4. Manual smoke test: start the server, send an auth request with `client_id=https://192.168.1.1/` → verify error page shown (not redirect)

## Files Changed

| File | Change |
|------|--------|
| `src/Talos.Web/Services/UrlValidator.cs` | Add `HasDotSegments()` call in `IsValidClientId()` |
| `src/Talos.Web/Services/AuthorizationService.cs` | Add `IsValidClientId()` check after null check |
| `tests/Talos.Web.Tests/Validators/UrlValidatorTests.cs` | Expand `IsValidClientId` test cases |
| `tests/Talos.Web.Tests/Services/AuthorizationServiceClientIdTests.cs` | New file — service-level integration tests |

## Estimated Scope

- ~5 lines production code changed
- ~150 lines test code added
- Low risk of regression — additive validation before existing flow
