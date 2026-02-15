# Fix Plan: GAP-4 — Implement client_id Fetching & Discovery

**Status:** ✅ Complete
**Spec requirement:** DISC-5, DISC-6, DISC-8, DISC-9, AUTH-9, AUTH-11, SEC-1, SEC-2
**Severity:** P1 — High
**Related gaps:** GAP-12 (SSRF protection), GAP-18 (localhost fetch blocking)
**Depends on:** GAP-2 (client_id validation) ✅ — already done

---

## Problem

The server never fetches the `client_id` URL. This means:

1. **No client app info on consent screen** — The consent page shows only the hostname extracted from the `client_id` URL (`new Uri(pending.ClientId).Host`). The spec says the server SHOULD fetch the client_id to discover the client's name, logo, and redirect URIs (DISC-5, AUTH-11, SEC-1).

2. **Cross-origin redirect URIs always rejected** — `UrlValidator.IsValidRedirectUri()` currently rejects any redirect_uri with a different scheme/host/port from the client_id. The spec says if they differ, the server MUST verify the redirect URL against the client's published `redirect_uri` list (DISC-9). Without fetching, we can't do this check, so we reject all cross-origin redirects.

3. **No client metadata verification** — If a client provides a JSON metadata document at its `client_id` URL, the spec requires verifying that the `client_id` in the document matches and that `client_uri` is a prefix of `client_id` (DISC-8).

## Spec Requirements

| ID | Requirement | Level |
|----|-------------|-------|
| DISC-5 | Server SHOULD fetch `client_id` URL to discover client info | SHOULD |
| DISC-6 | If `client_id` is localhost/loopback, MUST NOT fetch | MUST NOT |
| DISC-8 | If JSON metadata, verify `client_id` matches and `client_uri` is prefix | MUST |
| DISC-9 | Cross-origin `redirect_uri` must be verified against published list | MUST |
| AUTH-9 | Consent prompt MUST indicate which application is requesting access | MUST |
| AUTH-11 | Server SHOULD fetch `client_id` to display app info | SHOULD |
| SEC-1 | Server SHOULD fetch `client_id` to display app name/logo | SHOULD |
| SEC-2 | Server SHOULD display full `client_id` on authorization UI | SHOULD |

### Client Metadata Format (§4.2.1 — JSON)

The spec defines that a client MAY serve a JSON document at its `client_id` URL with `Content-Type: application/json`. The document:

```json
{
  "client_id": "https://app.example.com/",
  "client_name": "Example App",
  "client_uri": "https://app.example.com/",
  "logo_uri": "https://app.example.com/logo.png",
  "redirect_uris": ["https://app.example.com/callback", "https://other.example.com/redirect"]
}
```

- `client_id` MUST match the request client_id
- `client_uri` MUST be a prefix of `client_id`
- `redirect_uris` is used for cross-origin redirect validation

### Client Info from HTML (§4.2.2 — h-app microformat, legacy)

If the client_id URL serves HTML, the server can parse `h-app` microformat for `name`, `logo`, `url`.

---

## Changes

### 1. Create `ClientInfo` model

**File:** `src/Talos.Web/Models/ClientInfo.cs` (new)

```csharp
namespace Talos.Web.Models;

/// <summary>
/// Discovered client application metadata, fetched from the client_id URL.
/// Can be populated from either a JSON metadata document (§4.2.1) or
/// HTML h-app microformat (§4.2.2).
/// </summary>
public class ClientInfo
{
    public string ClientId { get; set; } = "";
    public string? ClientName { get; set; }
    public string? ClientUri { get; set; }
    public string? LogoUri { get; set; }
    public List<string> RedirectUris { get; set; } = [];

    /// <summary>Whether the client was fetched successfully (false = used defaults).</summary>
    public bool WasFetched { get; set; }
}
```

### 2. Create `IClientDiscoveryService` interface and implementation

**File:** `src/Talos.Web/Services/IClientDiscoveryService.cs` (new)

```csharp
namespace Talos.Web.Services;

public interface IClientDiscoveryService
{
    /// <summary>
    /// Fetches and parses client metadata from the client_id URL.
    /// Returns a default ClientInfo (with just the client_id) if the URL
    /// cannot be fetched, is localhost, or returns unrecognized content.
    /// </summary>
    Task<ClientInfo> DiscoverClientAsync(string clientId);
}
```

**File:** `src/Talos.Web/Services/ClientDiscoveryService.cs` (new)

Key behaviors:
- **Localhost/loopback check (DISC-6):** If `client_id` host is `localhost`, `127.0.0.1`, or `[::1]`, return default `ClientInfo` without fetching. This also addresses GAP-18.
- **HTTP fetch:** Use `IHttpClientFactory` named client `"ClientDiscovery"` with timeout (e.g. 10s) and user-agent.
- **Content negotiation:** Send `Accept: application/json, text/html` header.
- **JSON path:** If response `Content-Type` is `application/json`:
  - Deserialize to a DTO
  - Verify `client_id` in document matches request `client_id` (DISC-8)
  - Verify `client_uri` is a prefix of `client_id` (DISC-8)
  - Extract `client_name`, `logo_uri`, `redirect_uris`
- **HTML path:** If response is `text/html`:
  - Use `IMicroformatsService.Parse()` to extract `h-app` data
  - Look for `name`, `logo`, `url` properties in the first h-app item
  - (This requires adding h-app parsing to `MicroformatsResult` — see change 3)
- **Fallback:** If fetch fails (network error, non-2xx, unsupported content type), return default `ClientInfo` with `WasFetched = false`. Client discovery is SHOULD-level, not MUST — failures should not block the auth flow.
- **Logging:** Log warnings for fetch failures, info for successful discovery.

### 3. Extend `MicroformatsResult` with h-app data

**File:** `src/Talos.Web/Services/MicroformatsResult.cs`

Add fields for client application info from h-app:

```csharp
/// <summary>
/// Client application info from h-app microformat (if present).
/// </summary>
public string? AppName { get; set; }
public string? AppLogoUrl { get; set; }
public string? AppUrl { get; set; }
```

**File:** `src/Talos.Web/Services/MicroformatsService.cs`

The project already uses the `Microformats` NuGet package (v1.1.2) which provides full mf2 parsing including `h-app`. The parsed result exposes `Items` (an array of all h-* items) with `.Type` to identify the microformat type and `.Get()` / `.Get<T>()` to access properties.

After existing rel parsing, add h-app extraction using the library's API:

```csharp
// Extract h-app client info (IndieAuth §4.2.2)
var hApp = parsed.Items
    .FirstOrDefault(i => i.Type != null && i.Type.Contains("h-app"));
if (hApp != null)
{
    var names = hApp.Get(Props.NAME);
    if (names.Length > 0)
        result.AppName = names[0];

    var logos = hApp.Get(Props.LOGO);
    if (logos.Length > 0)
        result.AppLogoUrl = ResolveUrl(logos[0], baseUrl);

    var urls = hApp.Get(Props.URL);
    if (urls.Length > 0)
        result.AppUrl = ResolveUrl(urls[0], baseUrl);
}
```

`Props.NAME`, `Props.LOGO`, and `Props.URL` are well-known property constants from the Microformats library. The `.Get()` method returns `string[]` — we take the first value if present. Logo and URL values are resolved against the base URL using the existing `ResolveUrl` helper (already private in `MicroformatsService`).

### 4. Register `ClientDiscoveryService` in DI

**File:** `src/Talos.Web/Program.cs`

Add named HTTP client and service registration:

```csharp
builder.Services.AddHttpClient("ClientDiscovery", client =>
{
    client.DefaultRequestHeaders.Add("User-Agent", "Talos-IndieAuth");
    client.DefaultRequestHeaders.Add("Accept", "application/json, text/html");
    client.Timeout = TimeSpan.FromSeconds(10);
});

builder.Services.AddScoped<IClientDiscoveryService, ClientDiscoveryService>();
```

### 5. Integrate into `AuthorizationService`

**File:** `src/Talos.Web/Services/AuthorizationService.cs`

- Inject `IClientDiscoveryService` into the constructor.
- After `IsProfileHostAllowed` check and before profile discovery, call `clientDiscovery.DiscoverClientAsync(request.ClientId)`.
- **Cross-origin redirect_uri resolution:** If `IsValidRedirectUri` returns false AND the redirect_uri differs only in scheme/host/port (not structurally invalid), check if the `ClientInfo.RedirectUris` list contains an exact match. If yes, allow it.
- Store client metadata in the pending authentication entity for use on the consent screen.

The flow becomes:

```
1. Validate client_id (existing)
2. Validate redirect_uri — same-origin check (existing)
3. If redirect_uri failed same-origin:
   a. Fetch client_id → get ClientInfo
   b. Check if redirect_uri is in ClientInfo.RedirectUris (exact match)
   c. If match → proceed; if no match → reject with redirectUriUntrusted
4. If redirect_uri passed same-origin → optionally fetch client_id for display
5. Continue with profile discovery...
```

**Important design decision:** The client_id fetch for display purposes is SHOULD-level and must not block the flow. If fetching fails, use defaults (hostname as name, no logo). But the redirect_uri cross-origin check is MUST-level — if the redirect_uri is cross-origin and we can't verify it, we MUST reject.

### 6. Store client metadata in pending authentication

**File:** `src/Talos.Web/Data/Entities/Entities.cs`

Add to `PendingAuthenticationEntity`:

```csharp
public string? ClientName { get; set; }
public string? ClientLogoUri { get; set; }
```

**File:** `src/Talos.Web/Models/AuthorizationRequest.cs`

Add to `PendingAuthentication` model:

```csharp
public string? ClientName { get; set; }
public string? ClientLogoUri { get; set; }
```

### 7. Update consent endpoint to use stored client info

**File:** `src/Talos.Web/Controllers/AuthController.cs`

In `GetConsentInfo`, replace the current hostname extraction:

```csharp
// Before (current):
var clientName = new Uri(pending.ClientId).Host;

// After:
var clientName = pending.ClientName ?? new Uri(pending.ClientId).Host;
```

And include `logoUrl`:

```csharp
client = new
{
    clientId = pending.ClientId,
    name = clientName,
    url = pending.ClientId,
    logoUrl = pending.ClientLogoUri
}
```

### 8. Update `IsValidRedirectUri` to accept verified redirect URIs

**File:** `src/Talos.Web/Services/UrlValidator.cs`

Add new overload or modify the flow. The cleanest approach: add a separate method for cross-origin validation:

```csharp
/// <summary>
/// Validates a cross-origin redirect_uri against a list of published redirect URIs
/// from the client's metadata document. Exact string match after normalization.
/// </summary>
public static bool IsRedirectUriInPublishedList(string redirectUri, IReadOnlyList<string> publishedRedirectUris)
{
    // Exact match per DISC-9 (after any relative URL resolution, which is
    // handled by the caller since we already have absolute URLs)
    return publishedRedirectUris.Any(published =>
        string.Equals(redirectUri, published, StringComparison.Ordinal));
}
```

### 9. Database migration

Add a migration for the new columns on `PendingAuthenticationEntity`. Since this uses SQLite with `EnsureCreated()`, the simplest approach is to add the migration. If the DB is recreated on each deploy (dev mode), no explicit migration is needed. For production, add an EF Core migration.

---

## Tests

### Unit tests: `ClientDiscoveryServiceTests.cs` (new)

| Test | Scenario | Expected |
|------|----------|----------|
| `DiscoverClientAsync_LocalhostClientId_DoesNotFetch` | `http://localhost:8080/` | Returns default ClientInfo, `WasFetched=false`, no HTTP request |
| `DiscoverClientAsync_LoopbackIpv4_DoesNotFetch` | `http://127.0.0.1/` | Same as above |
| `DiscoverClientAsync_JsonMetadata_ParsesCorrectly` | JSON response with all fields | `ClientName`, `LogoUri`, `RedirectUris` populated |
| `DiscoverClientAsync_JsonMetadata_ClientIdMismatch_ReturnsFallback` | `client_id` in JSON doesn't match | Returns default, logs warning |
| `DiscoverClientAsync_JsonMetadata_ClientUriNotPrefix_ReturnsFallback` | `client_uri` is not prefix of `client_id` | Returns default, logs warning |
| `DiscoverClientAsync_HtmlWithHApp_ParsesNameAndLogo` | HTML with h-app microformat | Extracts name and logo |
| `DiscoverClientAsync_HtmlWithoutHApp_ReturnsFallback` | HTML without h-app | Returns default with `WasFetched=true` but no name/logo |
| `DiscoverClientAsync_HttpError_ReturnsFallback` | 404 response | Returns default, `WasFetched=false` |
| `DiscoverClientAsync_Timeout_ReturnsFallback` | Request times out | Returns default, `WasFetched=false` |
| `DiscoverClientAsync_RedirectUris_ExtractedFromJson` | JSON with `redirect_uris` array | `RedirectUris` list populated |

### Unit tests: `UrlValidatorTests.cs` (additions)

| Test | Scenario | Expected |
|------|----------|----------|
| `IsRedirectUriInPublishedList_ExactMatch_ReturnsTrue` | URI in list | true |
| `IsRedirectUriInPublishedList_NoMatch_ReturnsFalse` | URI not in list | false |
| `IsRedirectUriInPublishedList_CaseSensitive_ReturnsFalse` | Case differs | false (exact match) |
| `IsRedirectUriInPublishedList_EmptyList_ReturnsFalse` | Empty list | false |

### Integration tests: `AuthorizationServiceClientDiscoveryTests.cs` (new)

| Test | Scenario | Expected |
|------|----------|----------|
| `CreateAuthorizationAsync_CrossOriginRedirect_VerifiedViaClientMetadata_Succeeds` | redirect_uri differs from client_id host, but appears in fetched `redirect_uris` | Proceeds |
| `CreateAuthorizationAsync_CrossOriginRedirect_NotInMetadata_Rejected` | redirect_uri cross-origin, not in metadata | `invalid_request`, `RedirectUriUntrusted=true` |
| `CreateAuthorizationAsync_CrossOriginRedirect_FetchFails_Rejected` | redirect_uri cross-origin, client fetch fails | `invalid_request`, `RedirectUriUntrusted=true` |
| `CreateAuthorizationAsync_SameOriginRedirect_NoFetchNeeded_Succeeds` | redirect_uri same-origin | Proceeds (client may or may not be fetched for display) |
| `CreateAuthorizationAsync_ClientNameFromMetadata_StoredInSession` | JSON metadata with `client_name` | `PendingAuthentication.ClientName` populated |
| `CreateAuthorizationAsync_LocalhostClientId_NoFetch_Succeeds` | `http://localhost/` | No HTTP request, default client info |

---

## Files Changed

| File | Change |
|------|--------|
| `src/Talos.Web/Models/ClientInfo.cs` | **New** — Client metadata model |
| `src/Talos.Web/Services/IClientDiscoveryService.cs` | **New** — Interface |
| `src/Talos.Web/Services/ClientDiscoveryService.cs` | **New** — Implementation (fetch, parse JSON/HTML, localhost guard) |
| `src/Talos.Web/Services/MicroformatsResult.cs` | Add h-app fields (`AppName`, `AppLogoUrl`, `AppUrl`) |
| `src/Talos.Web/Services/MicroformatsService.cs` | Add h-app extraction from parsed microformats |
| `src/Talos.Web/Services/UrlValidator.cs` | Add `IsRedirectUriInPublishedList()` method |
| `src/Talos.Web/Services/AuthorizationService.cs` | Inject `IClientDiscoveryService`, integrate into flow |
| `src/Talos.Web/Data/Entities/Entities.cs` | Add `ClientName`, `ClientLogoUri` to entity |
| `src/Talos.Web/Models/AuthorizationRequest.cs` | Add `ClientName`, `ClientLogoUri` to `PendingAuthentication` |
| `src/Talos.Web/Controllers/AuthController.cs` | Use stored client name/logo in consent response |
| `src/Talos.Web/Program.cs` | Register `ClientDiscoveryService` + named HTTP client |
| `tests/.../ClientDiscoveryServiceTests.cs` | **New** — Unit tests (~10 tests) |
| `tests/.../Validators/UrlValidatorTests.cs` | Add `IsRedirectUriInPublishedList` tests (~4 tests) |
| `tests/.../Services/AuthorizationServiceClientDiscoveryTests.cs` | **New** — Integration tests (~6 tests) |

## Estimated Scope

- ~200 lines production code (new service + modifications)
- ~350 lines test code
- Medium risk — touches the authorization flow, but with fallback behavior (fetch failures don't break the flow)
- Also partially addresses GAP-18 (localhost fetch blocking) via the DISC-6 guard

## Design Decisions

1. **Fetch timing:** Client is fetched during `CreateAuthorizationAsync`, not lazily on the consent page. This ensures cross-origin redirect_uri validation happens before any state is created.

2. **Failure is non-fatal (for display):** If the fetch fails, the flow continues with default client info (hostname). But if the redirect_uri is cross-origin and we can't verify it, the flow MUST fail.

3. **No caching in v1:** The plan does not include caching of client metadata. This can be added later per Cache-Control headers. Each authorization request will fetch the client_id URL fresh. This is simpler and avoids cache invalidation issues.

4. **JSON preferred over HTML:** If the response has `Content-Type: application/json`, parse as JSON metadata. Otherwise, try HTML/h-app. This follows the spec's preference order.

5. **h-app extraction is best-effort:** Many clients won't have h-app markup. That's fine — the spec says the server SHOULD display the full `client_id` URL (which we already do).
