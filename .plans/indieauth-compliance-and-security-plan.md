# IndieAuth Specification Compliance & Security Plan

**Spec version:** IndieWeb Living Standard, 11 July 2024
**Spec URL:** https://indieauth.spec.indieweb.org/
**Date created:** 2025-02-15

---

## Part 1: Required Features per the IndieAuth Specification

The spec defines requirements using RFC 2119 keywords (MUST, SHOULD, MAY, etc.). This section catalogues every normative requirement an **Authorization Endpoint + Token Endpoint** implementation must satisfy, grouped by spec section.

---

### §3 — Identifiers

| ID | Requirement | Level | Spec Section |
|----|-------------|-------|--------------|
| ID-1 | Issuer identifier MUST use `https` scheme, no query/fragment, and MUST be a prefix of the `indieauth-metadata` URL | MUST | §3.1 |
| ID-2 | Profile URLs MUST have `https` or `http` scheme | MUST | §3.2 |
| ID-3 | Profile URLs MUST contain a path component (`/` is valid) | MUST | §3.2 |
| ID-4 | Profile URLs MUST NOT contain single-dot or double-dot path segments | MUST NOT | §3.2 |
| ID-5 | Profile URLs MUST NOT contain a fragment component | MUST NOT | §3.2 |
| ID-6 | Profile URLs MUST NOT contain username or password components | MUST NOT | §3.2 |
| ID-7 | Profile URLs MUST NOT contain a port | MUST NOT | §3.2 |
| ID-8 | Profile URL hosts MUST be domain names, MUST NOT be IPv4 or IPv6 addresses | MUST | §3.2 |
| ID-9 | Client IDs MUST have `https` or `http` scheme, contain a path, no dot-segments, no fragment, no userinfo; MAY contain port | MUST | §3.3 |
| ID-10 | Client ID hosts MUST be domain names or loopback (`127.0.0.1` / `[::1]`), MUST NOT be other IPs | MUST | §3.3 |
| ID-11 | URLs with no path component MUST be treated as having path `/` | MUST | §3.4 |
| ID-12 | Host component of URLs MUST be compared case-insensitively; SHOULD convert to lowercase | MUST/SHOULD | §3.4 |

### §4 — Discovery

| ID | Requirement | Level | Spec Section |
|----|-------------|-------|--------------|
| DISC-1 | Server MUST publish metadata at `indieauth-metadata` URL (via HTTP Link header or HTML `<link>`) | MUST | §4.1 |
| DISC-2 | `.well-known/oauth-authorization-server` path is RECOMMENDED for compatibility | RECOMMENDED | §4.1.1 |
| DISC-3 | Metadata MUST include `issuer`, `authorization_endpoint`, `token_endpoint` | MUST | §4.1.1 |
| DISC-4 | Metadata MUST include `code_challenge_methods_supported` (PKCE is required) | MUST | §4.1.1 |
| DISC-5 | Authorization server SHOULD fetch `client_id` URL to discover client info | SHOULD | §4.2 |
| DISC-6 | If `client_id` is localhost/loopback IP, authorization endpoint MUST NOT fetch it | MUST NOT | §4.2 |
| DISC-7 | Server SHOULD check `client_id` for SSRF (avoid internal IPs) before fetching | SHOULD | §4.2 |
| DISC-8 | If client provides JSON metadata, server MUST verify `client_id` in document matches request `client_id`, and `client_uri` MUST be a prefix of `client_id` | MUST | §4.2.1 |
| DISC-9 | If `redirect_uri` scheme/host/port differ from `client_id`, server MUST verify redirect URL against client's published `redirect_uri` list (exact match after resolving relative URLs) | MUST | §4.2.2 |

### §5.2 — Authorization Request

| ID | Requirement | Level | Spec Section |
|----|-------------|-------|--------------|
| AUTH-1 | All clients MUST use PKCE (RFC 7636) | MUST | §5.2 |
| AUTH-2 | `response_type=code` MUST be required | MUST | §5.2 |
| AUTH-3 | `client_id`, `redirect_uri`, `state` are required parameters | MUST | §5.2 |
| AUTH-4 | `code_challenge` and `code_challenge_method` are required | MUST | §5.2 |
| AUTH-5 | Authorization server MUST return unmodified `state` value back to client | MUST | §5.2 |
| AUTH-6 | If `scope` is omitted, authorization server MUST NOT issue an access token — only profile URL may be returned | MUST NOT | §5.2 |
| AUTH-7 | `me` parameter is optional but SHOULD be accepted as a hint | SHOULD | §5.2 |
| AUTH-8 | `me` from client is unverified data and MUST NOT be assumed valid at this stage | MUST NOT | §5.2 |
| AUTH-9 | Authorization prompt MUST indicate which application the user is signing in to | MUST | §5.2 |
| AUTH-10 | If `redirect_uri` scheme/host/port differ from `client_id`, server SHOULD verify redirect URL matches published list, and SHOULD block if not | SHOULD | §5.2 |
| AUTH-11 | Server SHOULD fetch `client_id` to display app info | SHOULD | §5.2 |

### §5.2.1 — Authorization Response

| ID | Requirement | Level | Spec Section |
|----|-------------|-------|--------------|
| RESP-1 | Authorization code MUST expire shortly (max 10 minutes recommended) | MUST | §5.2.1 |
| RESP-2 | Authorization code MUST be valid for only one use | MUST | §5.2.1 |
| RESP-3 | Response MUST include `code`, `state`, and `iss` parameters | MUST | §5.2.1 |
| RESP-4 | `state` MUST match the exact value from the request | MUST | §5.2.1 |
| RESP-5 | `iss` parameter MUST be included per RFC 9207 | MUST | §5.2.1 |

### §5.3 — Redeeming the Authorization Code

| ID | Requirement | Level | Spec Section |
|----|-------------|-------|--------------|
| REDEEM-1 | Request MUST use `grant_type=authorization_code` | MUST | §5.3.1 |
| REDEEM-2 | `code`, `client_id`, `redirect_uri`, `code_verifier` are all required | MUST | §5.3.1 |
| REDEEM-3 | Server MUST verify `client_id` and `redirect_uri` match the original request | MUST | §5.3.2/§5.3.3 |
| REDEEM-4 | Server MUST verify `code_verifier` hashes to `code_challenge` | MUST | §5.3.2/§5.3.3 |
| REDEEM-5 | If code was issued with no `scope`, token endpoint MUST NOT issue an access token | MUST NOT | §5.3.3 |
| REDEEM-6 | Access token response MUST include `access_token`, `me`; SHOULD include `expires_in` | MUST/SHOULD | §5.3.3 |
| REDEEM-7 | Profile URL response (at auth endpoint) MUST include `me` | MUST | §5.3.2 |
| REDEEM-8 | If code was issued with a `code_challenge`, exchange MUST include `code_verifier`; if issued without, exchange MUST NOT include `code_verifier` | MUST | §5.3.1 |

### §5.3.4 — Profile Information

| ID | Requirement | Level | Spec Section |
|----|-------------|-------|--------------|
| PROF-1 | When `profile` scope is requested, response MAY include a `profile` object with `name`, `photo`, `url` | MAY | §5.3.4 |
| PROF-2 | `email` scope MUST be requested alongside `profile` scope (not alone) | MUST | §5.3.4 |
| PROF-3 | Clients MUST NOT treat profile info as canonical or make auth decisions based on it | MUST NOT (client) | §5.3.4 |

### §5.4 — Authorization Server Confirmation

| ID | Requirement | Level | Spec Section |
|----|-------------|-------|--------------|
| CONFIRM-1 | If the returned `me` URL differs from the initially entered URL, the server/client MUST verify the returned `me` declares the same authorization server | MUST | §5.4 |

### §5.5 — Refresh Tokens

| ID | Requirement | Level | Spec Section |
|----|-------------|-------|--------------|
| REFRESH-1 | Short-lived access tokens + refresh tokens are RECOMMENDED | RECOMMENDED | §5.5 |
| REFRESH-2 | Refresh token request MUST include `grant_type=refresh_token`, `refresh_token`, `client_id` | MUST | §5.5.1 |
| REFRESH-3 | If new refresh token is issued, its scope MUST be identical to the original | MUST | §5.5.1 |
| REFRESH-4 | Refresh tokens SHOULD expire if unused for some time | SHOULD | §5.5.1 |

### §6 — Access Token Verification (Introspection)

| ID | Requirement | Level | Spec Section |
|----|-------------|-------|--------------|
| INTRO-1 | Introspection response MUST include `me` parameter | MUST | §6.1 |
| INTRO-2 | Introspection endpoint MUST require some form of authorization | MUST | §6.1 |
| INTRO-3 | If authorization is insufficient, server MUST respond with HTTP 401 | MUST | §6.1 |
| INTRO-4 | Response MUST include `active` (boolean) | MUST | §6.2 |
| INTRO-5 | For invalid tokens, MUST return 200 with `active: false` only | MUST | §6.2 |
| INTRO-6 | Token endpoints that intend to interoperate MUST use token introspection | MUST | §6 |

### §7 — Token Revocation

| ID | Requirement | Level | Spec Section |
|----|-------------|-------|--------------|
| REVOKE-1 | Server MUST implement revocation per RFC 7009 | MUST | §7 |
| REVOKE-2 | Revocation endpoint MUST return HTTP 200 for both successful and invalid token revocation | MUST | §7.1 |

### §8 — Accessing Protected Resources

| ID | Requirement | Level | Spec Section |
|----|-------------|-------|--------------|
| RESOURCE-1 | Resource server MUST validate token, check expiry, and verify scope | MUST | §8 |
| RESOURCE-2 | Error responses must use appropriate HTTP status codes (400, 401, 403) | SHOULD | §8.1 |

### §9 — User Information Endpoint (Optional)

| ID | Requirement | Level | Spec Section |
|----|-------------|-------|--------------|
| USERINFO-1 | If implemented, GET to userinfo endpoint with bearer token returns profile JSON | OPTIONAL | §9 |
| USERINFO-2 | Requires `profile` and/or `email` scopes | MUST (if implemented) | §9 |
| USERINFO-3 | Must be discoverable via `userinfo_endpoint` in metadata | MUST (if implemented) | §9 |

### §10 — Security Considerations

| ID | Requirement | Level | Spec Section |
|----|-------------|-------|--------------|
| SEC-1 | Server SHOULD fetch `client_id` to display app name/logo to user | SHOULD | §10.1 |
| SEC-2 | Server SHOULD display full `client_id` on authorization UI | SHOULD | §10.1 |
| SEC-3 | If `redirect_uri` scheme/host/port differ from `client_id`, server MUST verify redirect URL or display it to user | MUST | §10.1 |

---

## Part 2: Current Implementation Audit Findings (Preliminary)

Based on a code review of the Talos codebase, here are the key observations:

### What's Implemented
- ✅ Authorization endpoint (`/auth`) with GET (authorization request) and POST (code verification)
- ✅ Token endpoint (`/token`) with authorization_code and refresh_token grants
- ✅ PKCE (S256 only) — required on all requests
- ✅ Metadata endpoint at `/.well-known/oauth-authorization-server`
- ✅ Token introspection at `/token/introspect`
- ✅ Token revocation at `/token/revoke`
- ✅ Refresh token rotation (old token revoked on use)
- ✅ Authorization code expiry (configurable, default 10 min)
- ✅ Authorization code single-use enforcement
- ✅ Rate limiting on auth and token endpoints
- ✅ Security headers (CSP, X-Frame-Options, etc.)
- ✅ Profile discovery via rel="me" + microformats
- ✅ URL validation helpers (UrlValidator)
- ✅ PKCE verifier format validation (43-128 chars, unreserved chars)

### Gaps & Issues Identified

| ID | Gap | Severity | Spec Ref |
|----|-----|----------|----------|
| GAP-1 | ~~**`iss` parameter missing from authorization response**~~ — **FIXED** ✅ `iss` parameter now included in redirect URL | ~~HIGH~~ | RESP-3, RESP-5 |
| GAP-2 | ~~**No client_id URL validation**~~ — **FIXED** ✅ `CreateAuthorizationAsync` now calls `UrlValidator.IsValidClientId()` before proceeding; `IsValidClientId` enhanced with dot-segment check; invalid client_id sets `RedirectUriUntrusted` flag | ~~MEDIUM~~ | ID-9, ID-10 |
| GAP-3 | ~~**No redirect_uri validation**~~ — **FIXED** ✅ `redirect_uri` now validated in `CreateAuthorizationAsync`; dangerous schemes blocked; cross-origin rejected; controller no longer redirects errors to untrusted URIs | ~~HIGH~~ | DISC-9, AUTH-10, SEC-3 |
| GAP-4 | **No client_id fetching/discovery** — server never fetches the `client_id` URL to discover client metadata or display app info to user | MEDIUM | DISC-5, AUTH-11, SEC-1, SEC-2 |
| GAP-5 | ~~**Profile URL validation incomplete**~~ — **FIXED** ✅ New `IsValidProfileUrl()` in `UrlValidator` checks all §3.2 requirements (scheme, path, dot-segments, fragment, userinfo, port, IP host); wired into `CreateAuthorizationAsync` | ~~MEDIUM~~ | ID-2 through ID-8 |
| GAP-6 | ~~**No scope-gating on token issuance**~~ — **FIXED** ✅ Token endpoint now rejects codes with empty scope; returns `invalid_grant` error directing client to use auth endpoint | ~~HIGH~~ | REDEEM-5, AUTH-6 |
| GAP-7 | ~~**Introspection endpoint has no authorization**~~ — **FIXED** ✅ `/token/introspect` now requires `Authorization: Bearer <secret>` with configurable `IntrospectionSecret`; constant-time comparison; fail-closed if unconfigured | ~~HIGH~~ | INTRO-2, INTRO-3 |
| GAP-8 | **Metadata missing `authorization_response_iss_parameter_supported`** — should be `true` since `iss` is required | LOW | §4.1.1 |
| GAP-9 | **No `indieauth-metadata` link relation** served from user profile pages — the `.well-known` endpoint exists but profile pages don't advertise it | MEDIUM | DISC-1 |
| GAP-10 | **No userinfo endpoint** — optional but listed in metadata considerations | LOW | USERINFO-1 |
| GAP-11 | **Profile information not returned** — even when `profile` scope requested, neither auth endpoint nor token endpoint returns `profile` object | LOW | PROF-1 |
| GAP-12 | **No SSRF protection** on `client_id` fetching (if implemented) or on profile discovery `HttpClient` — no checks for internal/private IPs | MEDIUM | DISC-7 |
| GAP-13 | **Refresh token grant doesn't validate `client_id` is required** — spec says `client_id` is required in refresh request, but implementation only checks "if provided" | MEDIUM | REFRESH-2 |
| GAP-14 | **`me` parameter treated as required** — spec says `me` is optional (SHOULD be provided, not MUST) | LOW | AUTH-7 |
| GAP-15 | **No `iat` in introspection response** — spec lists it as optional but recommended | LOW | §6.2 |
| GAP-16 | ~~**`active` returned as string**~~ — **FIXED** ✅ Introspection now uses strongly-typed `IntrospectionResponse` with `[JsonPropertyName]` attributes; `active` guaranteed to be JSON boolean | ~~MEDIUM~~ | INTRO-4 |
| GAP-17 | ~~**Default scope behavior**~~ — **FIXED** ✅ `ParseScopes` now returns empty list for null/empty scope; no default scope injected | ~~MEDIUM~~ | AUTH-6 |
| GAP-18 | **Localhost `client_id` not blocked from fetching** — spec says MUST NOT fetch localhost `client_id` URLs | MEDIUM | DISC-6 |
| GAP-19 | **No `revocation_endpoint_auth_methods_supported`** in metadata — spec says this SHOULD be `["none"]` if a revocation endpoint is provided | LOW | §4.1.1 |
| GAP-20 | **`introspection_endpoint_auth_methods_supported`** missing from metadata | LOW | §4.1.1 |

---

## Part 3: Compliance & Security Verification Plan

### Phase 1: Critical Spec Compliance Fixes

Priority: **Must fix before any deployment**

- [x] **1.1 — Add `iss` parameter to authorization response** (GAP-1) ✅
  - In `AuthorizationService.CreateAuthorizationCodeAsync`, added `&iss=<encoded issuer URL>` to the redirect URL
  - The issuer value matches the `issuer` in the metadata endpoint (`TalosSettings.BaseUrl.TrimEnd('/')`)
  - 4 tests added in `AuthorizationServiceIssParameterTests.cs` — all passing
  - See [gap-1-iss-parameter.md](gap-1-iss-parameter.md) for details

- [x] **1.2 — Add redirect_uri validation** (GAP-3) ✅
  - `UrlValidator.IsValidRedirectUri()` now called in `CreateAuthorizationAsync` — blocks dangerous schemes, non-HTTPS, cross-origin, fragments, userinfo, and dot-segments
  - `HasDotSegments()` operates on raw string to avoid .NET Uri normalization
  - `AuthorizationResult.RedirectUriUntrusted` flag prevents controller from redirecting errors to an untrusted URI
  - ~22 UrlValidator tests + 7 service-level integration tests added (153 total tests passing)
  - Cross-origin redirect URIs rejected until GAP-4 (client metadata fetching) is implemented
  - See [gap-3-redirect-uri-validation.md](gap-3-redirect-uri-validation.md) for details

- [x] **1.3 — Enforce no-token-for-empty-scope** (GAP-6, GAP-17) ✅
  - `ParseScopes` fixed to return empty list for null/empty/whitespace scope — no default `profile` injected
  - Token endpoint now rejects codes with no scopes: returns `invalid_grant` with message directing client to use auth endpoint
  - Auth endpoint POST continues to return only `me` regardless of scope (correct behavior)
  - 6 scope-parsing tests + 5 token endpoint scope-gating tests added (173 total tests passing)
  - See [gap-6-17-scope-gating.md](gap-6-17-scope-gating.md) for details

- [x] **1.4 — Require authorization on introspection endpoint** (GAP-7) ✅
  - Configurable `IntrospectionSecret` added to `IndieAuthSettings`; resource servers present it as `Authorization: Bearer <secret>`
  - Fail-closed: if no secret configured, all introspection requests return 401
  - Constant-time comparison via `CryptographicOperations.FixedTimeEquals` prevents timing attacks
  - Metadata updated with `introspection_endpoint_auth_methods_supported: ["Bearer"]` (partially addresses GAP-20)
  - 11 new auth tests + 3 existing tests updated to include auth header (184 total tests passing)
  - See [gap-7-introspection-auth.md](gap-7-introspection-auth.md) for details

- [x] **1.5 — Fix introspection `active` field type** (GAP-16) ✅
  - Replaced anonymous objects with strongly-typed `IntrospectionResponse` class using `[JsonPropertyName]` attributes
  - `Active` is `bool` — guaranteed JSON boolean; property names guaranteed snake_case (`client_id`, not `clientId`)
  - Inactive response serializes to `{"active":false}` only (null/default fields omitted via `[JsonIgnore]`)
  - 6 serialization tests + 3 controller-level tests added (173 total tests passing)
  - See [gap-16-introspection-active-field.md](gap-16-introspection-active-field.md) for details

### Phase 2: Important Compliance Improvements

Priority: **Should fix before production use**

- [x] **2.1 — Add client_id URL validation** (GAP-2) ✅
  - `UrlValidator.IsValidClientId()` now called in `CreateAuthorizationAsync` after null check and before redirect_uri validation
  - `IsValidClientId()` enhanced with `HasDotSegments()` check per spec §3.3
  - Invalid `client_id` returns `invalid_request` error with `RedirectUriUntrusted=true` (controller shows error page, does not redirect)
  - ~12 expanded validator tests + 7 service-level integration tests added (204 total tests passing)
  - See [gap-2-client-id-validation.md](gap-2-client-id-validation.md) for details

- [x] **2.2 — Strengthen Profile URL validation** (GAP-5) ✅
  - New `UrlValidator.IsValidProfileUrl()` method validates all §3.2 requirements: https/http scheme, path present, no dot-segments, no fragment, no userinfo, no non-default port, no IP hosts (no loopback exception)
  - Wired into `CreateAuthorizationAsync` after the `me` null check and before `IsProfileHostAllowed`
  - `RedirectUriUntrusted` is NOT set for `me` errors (client_id and redirect_uri already validated by this point)
  - 7 valid + 15 invalid validator tests + 9 service-level integration tests added; 1 existing test updated (235 total tests passing)
  - See [gap-5-profile-url-validation.md](gap-5-profile-url-validation.md) for details

- [x] **2.3 — Implement client_id fetching** (GAP-4) ✅
  - Created `IClientDiscoveryService` / `ClientDiscoveryService` to fetch and parse client metadata from `client_id` URL
  - Supports JSON metadata document (§4.2.1) with `JsonPropertyName` attributes for snake_case mapping
  - Supports HTML with h-app microformats (§4.2.2) via existing Microformats NuGet package
  - DISC-6: Loopback client_id URLs are not fetched
  - DISC-8: `client_id` and `client_uri` in JSON document validated against request
  - DISC-9: Cross-origin redirect_uris verified against published `redirect_uris` list
  - Client name/logo stored in `PendingAuthenticationEntity` and displayed on consent screen
  - EF migration `AddClientMetadataToPendingAuth` adds `ClientName`, `ClientLogoUri` columns
  - 38 new tests added (14 ClientDiscoveryService, 5 UrlValidator, 8 MicroformatsServiceHApp, 11 AuthorizationServiceClientDiscovery); 273 total tests passing
  - See [gap-4-client-discovery.md](gap-4-client-discovery.md) for details

- [x] **2.4 — Make `me` parameter optional** (GAP-14)
  - `me` is now optional per IndieAuth spec §5.2 (AUTH-7). When absent, the server redirects to `/enter-profile` with original auth params + client display info. The frontend form collects the profile URL and replays the `/auth` request with `me` appended — no new API endpoints or session state needed.
  - New `EnterProfileView.vue` with `ClientCard`, form input, and redirect-through logic
  - `BuildEnterProfileRedirect` helper in `AuthorizationService` constructs the redirect URL
  - 12 new tests (AuthorizationServiceOptionalMeTests); 285 total tests passing
  - See [gap-14-optional-me.md](gap-14-optional-me.md) for details

- [x] **2.5 — Require client_id in refresh token grant** (GAP-13)
  - `client_id` is now required in the refresh token grant per IndieAuth spec §5.3.3 (REFRESH-2)
  - Added required check before token lookup; changed "if provided" mismatch check to unconditional
  - 7 new tests (TokenControllerRefreshTokenTests); 292 total tests passing

- [x] **2.6 — Add SSRF protection** (GAP-12)
  - Created `SsrfProtection` static class with `IsPrivateOrReservedAddress(IPAddress)` covering all RFC 1918 private ranges, loopback (127.0.0.0/8, ::1), link-local (169.254.0.0/16, fe80::/10), cloud metadata (169.254.169.254), carrier-grade NAT (100.64.0.0/10), unique local IPv6 (fc00::/7), multicast, documentation/test ranges, and IPv4-mapped IPv6 addresses
  - Created `SsrfProtection.CreateSsrfSafeHandler()` which returns a `SocketsHttpHandler` with a `ConnectCallback` that resolves DNS and blocks connections to private/reserved IPs at the transport level — preventing TOCTOU races and catching redirects
  - Wired into both `ProfileDiscovery` and `ClientDiscovery` HTTP clients in `Program.cs` via `ConfigurePrimaryHttpMessageHandler`
  - 49 new tests (SsrfProtectionTests) covering IPv4 private/loopback/link-local/CGN/multicast/reserved, IPv6 loopback/link-local/ULA/multicast, IPv4-mapped IPv6, boundary values, and public address allow-list; 341 total tests passing

- [x] **2.7 — Don't fetch localhost client_id** (GAP-18)
  - Already implemented: `ClientDiscoveryService.IsLoopback()` checks URL hostname for localhost/127.0.0.1/[::1] before fetching
  - Already tested: `DiscoverClientAsync_LoopbackClientId_DoesNotFetch` theory test with 4 inline data cases (localhost, localhost:8080, 127.0.0.1, [::1])
  - Now also protected at transport level by the SSRF-safe `SocketsHttpHandler` (belt-and-suspenders)

### Phase 3: Metadata & Optional Features

Priority: **Nice to have for full compliance**

- [ ] **3.1 — Add `iss` and auth method fields to metadata** (GAP-8, GAP-19, GAP-20)
  - Add `authorization_response_iss_parameter_supported: true`
  - Add `revocation_endpoint_auth_methods_supported: ["none"]`
  - Add `introspection_endpoint_auth_methods_supported` with actual supported methods
  - Write test: verify all fields present in metadata response

- [ ] **3.2 — Serve `indieauth-metadata` link relation** (GAP-9)
  - If Talos serves user profile pages, add `Link: <metadata-url>; rel="indieauth-metadata"` HTTP header
  - Or add `<link rel="indieauth-metadata" href="...">` to HTML

- [ ] **3.3 — Implement profile information responses** (GAP-11)
  - When `profile` scope is granted, include `profile` object in auth/token response
  - Fields: `name`, `url`, `photo`, and optionally `email` (if `email` scope)
  - Source data from identity provider verification result
  - Write tests with profile/email scopes

- [ ] **3.4 — Implement userinfo endpoint** (GAP-10)
  - `GET /userinfo` with Bearer token
  - Return same profile object as §5.3.4
  - Require `profile` scope in the token
  - Advertise in metadata as `userinfo_endpoint`
  - Write tests: valid token with profile scope → profile JSON; missing scope → 403

- [ ] **3.5 — Add `iat` to introspection response** (GAP-15)
  - Extract `iat` claim from JWT and include in introspection response
  - Write test: introspection response includes `iat` as integer

### Phase 4: Security Hardening & Testing

- [ ] **4.1 — Authorization code injection tests**
  - Test that a code from one client cannot be used by a different client
  - Test that code cannot be reused after first redemption
  - Test that expired codes are rejected
  - Test that code_verifier mismatch is rejected

- [ ] **4.2 — CSRF protection tests**
  - Verify `state` parameter is required and validated
  - Test that mismatched `state` is rejected

- [ ] **4.3 — Open redirect prevention tests**
  - Test that arbitrary `redirect_uri` values are blocked
  - Test that `redirect_uri` not matching `client_id` host triggers verification
  - Test that JavaScript/data URIs in redirect_uri are rejected

- [ ] **4.4 — Token security tests**
  - Verify access tokens expire as configured
  - Verify refresh token rotation (old token invalidated)
  - Verify revoked refresh tokens cannot be used
  - Test token with tampered signature is rejected

- [ ] **4.5 — Rate limiting verification**
  - Verify auth endpoint rate limit (30/min) works
  - Verify token endpoint rate limit (20/min) works
  - Verify global rate limit (100/min) works
  - Test that rate-limited requests get 429 response

- [ ] **4.6 — Input validation & injection tests**
  - Test SQL injection via all string parameters (session_id, code, etc.)
  - Test XSS via client_id, redirect_uri, error_description parameters
  - Test path traversal in URL parameters
  - Test oversized inputs (very long URLs, scopes, etc.)

- [ ] **4.7 — PKCE implementation tests**
  - Verify code_verifier length validation (43-128 chars)
  - Verify code_verifier character set validation
  - Verify S256 challenge computation matches RFC 7636
  - Test that plain method is rejected
  - Test with known test vectors

- [ ] **4.8 — Timing attack resistance**
  - Verify authorization code comparison uses constant-time comparison
  - Verify refresh token comparison uses constant-time comparison

- [ ] **4.9 — TLS / transport security**
  - Verify HTTPS redirection is enabled in production
  - Verify HSTS is set in production
  - Verify security headers are present on all responses

- [ ] **4.10 — Session/state management**
  - Verify pending authentication sessions expire
  - Verify expired sessions are cleaned up
  - Test that session IDs are cryptographically random and sufficiently long

### Phase 5: Integration & Conformance Testing

- [ ] **5.1 — Test against indieauth.rocks**
  - Deploy to a test environment
  - Run through the [indieauth.rocks](https://indieauth.rocks/) test suite
  - Document and fix any failures

- [ ] **5.2 — Test with real IndieAuth clients**
  - Test with Micropub clients (e.g., Quill, Indigenous)
  - Test with IndieAuth login flows on known consumers
  - Verify interoperability with other IndieAuth implementations

- [ ] **5.3 — End-to-end flow tests**
  - Full authorization code flow (happy path)
  - Authorization code flow with profile-only (no token)
  - Refresh token flow
  - Token revocation flow
  - Token introspection flow
  - Error scenarios (denied consent, expired code, bad PKCE, etc.)

---

## Summary Priority Matrix

| Priority | Items | Impact |
|----------|-------|--------|
| **P0 — Critical** | GAP-1 (iss), GAP-3 (redirect validation), GAP-6 (scope gating), GAP-7 (introspection auth), GAP-16 (active bool) | Spec violations that could cause interop failures or security vulnerabilities |
| **P1 — High** | GAP-2 (client_id validation), GAP-5 (profile URL validation), GAP-12 (SSRF), GAP-13 (refresh client_id), GAP-18 (localhost fetch) | Security risks or significant spec deviations |
| **P2 — Medium** | GAP-4 (client discovery), GAP-14 (optional me), GAP-17 (default scope) | Spec compliance, user experience |
| **P3 — Low** | GAP-8, GAP-9, GAP-10, GAP-11, GAP-15, GAP-19, GAP-20 | Optional features, metadata completeness |
