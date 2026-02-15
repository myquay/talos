# Fix Plan: GAP-1 — Add `iss` parameter to authorization response

**Status:** Complete ✅
**Spec requirement:** RESP-3, RESP-5 (§5.2.1)
**Severity:** P0 — Critical
**Completed:** 2025-02-15
**Tests:** 4 new tests in `AuthorizationServiceIssParameterTests.cs`, all passing (108/108 total suite)

---

## Problem

`AuthorizationService.CreateAuthorizationCodeAsync` builds the redirect URL with only `code` and `state`:

```csharp
var redirectUrl = $"{pending.RedirectUri}?code={Uri.EscapeDataString(code)}&state={Uri.EscapeDataString(pending.State)}";
```

The IndieAuth spec (§5.2.1) and RFC 9207 require the `iss` parameter to also be included in the authorization response. This prevents authorization server mix-up attacks.

## Fix

### Production code change

**File:** `src/Talos.Web/Services/AuthorizationService.cs`, in `CreateAuthorizationCodeAsync`

Add the issuer value (from `TalosSettings.BaseUrl`, trimmed of trailing slash — same source as the metadata endpoint) to the redirect URL as an `iss` query parameter:

```csharp
var issuer = talosSettings.Value.BaseUrl.TrimEnd('/');
var redirectUrl = $"{pending.RedirectUri}?code={Uri.EscapeDataString(code)}&state={Uri.EscapeDataString(pending.State)}&iss={Uri.EscapeDataString(issuer)}";
```

No new dependencies — `talosSettings` is already injected into `AuthorizationService`.

### Test

**File:** `tests/Talos.Web.Tests/Services/AuthorizationServiceIssParameterTests.cs` (new)

Test cases:
1. `CreateAuthorizationCodeAsync` redirect URL contains `iss` query parameter
2. The `iss` value equals `TalosSettings.BaseUrl.TrimEnd('/')`
3. The `iss` value is properly URL-encoded

### Verification

- Run existing tests — must all pass (no regressions)
- Run new test — must pass
- Confirm `iss` value matches what `MetadataController.GetMetadata()` returns as `issuer`
