# Fix Plan: GAP-5 — Strengthen Profile URL Validation

**Status:** Complete ✅
**Spec requirement:** ID-2 through ID-8 (§3.2)
**Severity:** P1 — High
**Related gaps:** None (standalone, but validation also benefits GAP-12 SSRF protection)
**Completed:** 2026-02-15
**Tests:** 7 valid + 15 invalid validator tests + 9 service-level integration tests + 1 existing test updated, all passing (235/235 total suite)

---

## Problem

The `me` parameter (user's profile URL) is currently accepted with minimal validation. `CreateAuthorizationAsync` only checks it is non-empty, then passes it to `ProfileDiscoveryService.DiscoverProfileAsync` which normalizes it but does not validate against the IndieAuth spec §3.2 requirements.

There is no `IsValidProfileUrl()` method. The existing `NormalizeProfileUrl()` in both `UrlValidator` and `ProfileDiscoveryService` handles scheme defaulting and host lowercasing but does not reject invalid URLs.

This means profile URLs with ports, IP address hosts, dot-segments, fragments, or userinfo are all accepted and fetched.

## Spec Requirements (§3.2)

> Profile URLs:
> - MUST have either an `https` or `http` scheme (ID-2)
> - MUST contain a path component (`/` is valid) (ID-3)
> - MUST NOT contain single-dot or double-dot path segments (ID-4)
> - MUST NOT contain a fragment component (ID-5)
> - MUST NOT contain a username or password component (ID-6)
> - MUST NOT contain a port (ID-7)
> - Host MUST be a domain name, MUST NOT be IPv4 or IPv6 address (ID-8)

**Key difference from `client_id` (§3.3):** Profile URLs MUST NOT contain a port, while client IDs MAY contain a port. Profile URLs also do NOT have the loopback exception — all IPs are rejected.

---

## Changes

### 1. Add `IsValidProfileUrl()` to `UrlValidator`

**File:** `src/Talos.Web/Services/UrlValidator.cs`

Add a new method after `IsValidClientId()`. This intentionally does NOT reuse `IsValidHttpsUrl()` because that method has a localhost exception (allows HTTP for localhost) and allows ports, neither of which apply to profile URLs.

```csharp
/// <summary>
/// Validates a profile URL per IndieAuth specification §3.2.
/// Profile URLs have stricter requirements than client IDs:
/// no port allowed, no IP addresses (not even loopback).
/// </summary>
public static bool IsValidProfileUrl(string? url)
{
    if (string.IsNullOrWhiteSpace(url))
        return false;

    if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        return false;

    // ID-2: Must be https or http scheme
    if (uri.Scheme != "https" && uri.Scheme != "http")
        return false;

    // ID-3: Must contain a path component (at least /)
    if (string.IsNullOrEmpty(uri.AbsolutePath))
        return false;

    // ID-4: Must not contain dot-segments
    if (HasDotSegments(url))
        return false;

    // ID-5: Must not contain a fragment
    if (!string.IsNullOrEmpty(uri.Fragment))
        return false;

    // ID-6: Must not contain username or password
    if (!string.IsNullOrEmpty(uri.UserInfo))
        return false;

    // ID-7: Must not contain a port
    if (!uri.IsDefaultPort)
        return false;

    // ID-8: Host must be a domain name, not an IP address (no loopback exception)
    if (uri.HostNameType == UriHostNameType.IPv4 ||
        uri.HostNameType == UriHostNameType.IPv6)
        return false;

    return true;
}
```

**Note on ID-7 (port check):** `Uri.IsDefaultPort` returns `true` when no explicit port is present OR when the explicit port matches the scheme default (80 for http, 443 for https). This means `https://example.com:443/` would pass — which is debatable. A stricter check would inspect the raw URL string for a `:port` after the host. The plan proposes using `Uri.IsDefaultPort` as the initial implementation since `https://example.com:443/` and `https://example.com/` are semantically identical, but this should be noted in tests.

### 2. Wire up validation in `CreateAuthorizationAsync`

**File:** `src/Talos.Web/Services/AuthorizationService.cs`

Replace the current null check for `me` with a proper validation call. Insert after the `CodeChallengeMethod` check and before the `IsProfileHostAllowed` check:

```csharp
// Validate 'me' parameter (user's profile URL) per IndieAuth spec §3.2
if (string.IsNullOrEmpty(request.Me))
{
    return ErrorResult("invalid_request", "me (profile URL) is required");
}

if (!UrlValidator.IsValidProfileUrl(request.Me))
{
    return ErrorResult("invalid_request",
        "me is not a valid profile URL per the IndieAuth specification");
}

// Check if the profile host is allowed (personal server mode)
```

The `me` validation error does NOT set `redirectUriUntrusted` — by this point in the flow, both `client_id` and `redirect_uri` have already been validated, so it's safe to redirect the error back to the client.

### 3. Add comprehensive `IsValidProfileUrl` unit tests

**File:** `tests/Talos.Web.Tests/Validators/UrlValidatorTests.cs`

Add a new section after the existing `IsValidClientId` tests.

#### Valid profile URLs (should return `true`)
| Input | Rationale |
|-------|-----------|
| `https://example.com/` | Basic valid profile URL |
| `https://example.com/username` | Path without trailing slash |
| `https://example.com/users/1` | Deep path |
| `http://example.com/` | HTTP is allowed per §3.2 |
| `https://example.com/?query=value` | Query strings are not prohibited |

#### Invalid profile URLs (should return `false`)
| Input | Rationale | Spec Ref |
|-------|-----------|----------|
| `ftp://example.com/` | Wrong scheme | ID-2 |
| `mailto:user@example.com` | Wrong scheme | ID-2 |
| `https://example.com/foo/../bar` | Dot-segments | ID-4 |
| `https://example.com/./path` | Single-dot segment | ID-4 |
| `https://example.com/#fragment` | Fragment | ID-5 |
| `https://example.com/path#frag` | Fragment on path | ID-5 |
| `https://user:pass@example.com/` | Userinfo | ID-6 |
| `https://example.com:8443/` | Non-default port | ID-7 |
| `http://example.com:9090/` | Non-default port (HTTP) | ID-7 |
| `https://192.168.1.1/` | IPv4 address | ID-8 |
| `https://127.0.0.1/` | Loopback IPv4 (no exception for profiles) | ID-8 |
| `https://[::1]/` | Loopback IPv6 | ID-8 |
| `https://10.0.0.1/` | Private IPv4 | ID-8 |
| `""` | Empty | — |
| `null` | Null | — |
| `not-a-url` | Not a URL | — |

#### Edge cases to document (not test failures)
| Input | Result | Note |
|-------|--------|------|
| `https://example.com:443/` | `true` | Default port for HTTPS — `Uri.IsDefaultPort` returns true |
| `http://example.com:80/` | `true` | Default port for HTTP — `Uri.IsDefaultPort` returns true |

### 4. Add service-level integration tests

**File:** `tests/Talos.Web.Tests/Services/AuthorizationServiceProfileUrlTests.cs` (new)

Follow the same pattern as `AuthorizationServiceClientIdTests.cs`:

| Test | Input `me` | Expected |
|------|-----------|----------|
| `CreateAuthorizationAsync_ProfileUrlWithPort_ReturnsError` | `https://example.com:8443/` | `Success=false`, `Error=invalid_request`, error mentions `me` |
| `CreateAuthorizationAsync_ProfileUrlIsIpAddress_ReturnsError` | `https://192.168.1.1/` | `Success=false`, `Error=invalid_request` |
| `CreateAuthorizationAsync_ProfileUrlIsLoopback_ReturnsError` | `https://127.0.0.1/` | `Success=false`, `Error=invalid_request` |
| `CreateAuthorizationAsync_ProfileUrlWithFragment_ReturnsError` | `https://example.com/#frag` | `Success=false`, `Error=invalid_request` |
| `CreateAuthorizationAsync_ProfileUrlWithDotSegments_ReturnsError` | `https://example.com/foo/../bar` | `Success=false`, `Error=invalid_request` |
| `CreateAuthorizationAsync_ValidProfileUrl_ProceedsPastValidation` | `https://example.com/` | No `me`-related error |
| `CreateAuthorizationAsync_ValidHttpProfileUrl_ProceedsPastValidation` | `http://example.com/` | No `me`-related error |
| `CreateAuthorizationAsync_InvalidProfileUrl_DoesNotReachProfileDiscovery` | `https://192.168.1.1/` | Profile discovery mock never called |
| `CreateAuthorizationAsync_InvalidProfileUrl_RedirectUriStillTrusted` | `https://10.0.0.1/` | `RedirectUriUntrusted=false` (redirect_uri was already validated) |

---

## Verification

1. Run existing tests — ensure no regressions (especially `NormalizeProfileUrl` tests, `AuthorizationServiceAllowedHostsTests` which exercises the `me` parameter path)
2. Run new `IsValidProfileUrl` validator tests — all pass
3. Run new service-level tests — all pass
4. Manual check: `NormalizeProfileUrl()` in `ProfileDiscoveryService` and `UrlValidator` are unchanged — they normalize, not validate; validation is a separate concern applied earlier in the flow

## Files Changed

| File | Change |
|------|--------|
| `src/Talos.Web/Services/UrlValidator.cs` | Add `IsValidProfileUrl()` method |
| `src/Talos.Web/Services/AuthorizationService.cs` | Add `IsValidProfileUrl()` check after existing `me` null check |
| `tests/Talos.Web.Tests/Validators/UrlValidatorTests.cs` | Add `IsValidProfileUrl` test section (~16 cases) |
| `tests/Talos.Web.Tests/Services/AuthorizationServiceProfileUrlTests.cs` | New file — service-level integration tests (~9 tests) |

## Estimated Scope

- ~25 lines production code added
- ~200 lines test code added
- Low risk of regression — additive validation before existing flow
- `NormalizeProfileUrl` is not modified (normalization and validation are separate concerns)
