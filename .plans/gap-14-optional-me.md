# Fix Plan: GAP-14 — Make `me` Parameter Optional

**Status:** Complete  
**Spec requirement:** AUTH-7  
**Severity:** P2 — Medium (Low priority per gap analysis, but improves spec compliance)  
**Depends on:** None  
**Completed:** 16 Feb 2026 — 12 new tests; 285 total passing

---

## Problem

The IndieAuth spec §5.2 says the `me` parameter SHOULD be provided by the client, but it is **not required**. Talos currently rejects any authorization request that omits `me`:

```csharp
if (string.IsNullOrEmpty(request.Me))
{
    return ErrorResult("invalid_request", "me (profile URL) is required");
}
```

This means well-behaved clients that don't know the user's profile URL upfront (e.g. a generic IndieAuth client that lets the server ask the user) will be rejected.

When `me` is absent, the authorization server should prompt the user to enter their profile URL before proceeding with identity provider discovery.

## Spec Requirements

| ID | Requirement | Level | Spec Section |
|----|-------------|-------|--------------|
| AUTH-7 | `me` parameter is optional but SHOULD be accepted as a hint | SHOULD | §5.2 |

## Design: Redirect-Through Approach

Instead of introducing new backend endpoints or session states, take a simpler approach: when `me` is missing, validate everything else (client_id, redirect_uri, PKCE), fetch client info for display, then redirect to a frontend form. When the user submits their profile URL, the frontend redirects back to `/auth` with `me` appended to the original query parameters. The **existing flow handles the rest unchanged**.

### Current Flow (with `me`)

```
1. Client sends /auth?...&me=https://user.example.com/
2. AuthorizationService validates all params (client_id, redirect_uri, PKCE, me)
3. Discover providers from me URL → create pending session
4. Redirect to /select-provider or directly to OAuth provider
5. After auth → consent → code
```

### Proposed Flow (without `me`)

```
1. Client sends /auth?...  (no me parameter)
2. AuthorizationService validates client_id, redirect_uri, PKCE — all pass
3. Fetch client_id info for display (name, logo)
4. Return Success with RedirectUrl → /enter-profile?<original_params>&client_name=...&client_logo=...
5. Frontend shows "Enter your profile URL to log in to {client_name}" form
6. User enters profile URL, submits
7. Frontend redirects to /auth?<original_params>&me={entered_url}
8. Existing flow handles everything (validates me, discovers providers, creates session, etc.)
```

**Key insight:** No pending session is created, no new API endpoints, no new service methods. The frontend form just replays the original authorization request with `me` attached.

---

## Changes

### 1. Update `AuthorizationService.CreateAuthorizationAsync`

**File:** `src/Talos.Web/Services/AuthorizationService.cs` (lines ~95–130)

Replace the hard `me` required check with a conditional branch:

```csharp
// me parameter is optional per IndieAuth spec §5.2 (AUTH-7)
if (string.IsNullOrEmpty(request.Me))
{
    // Fetch client info so the enter-profile page can display the app name/logo
    clientInfo ??= await clientDiscovery.DiscoverClientAsync(request.ClientId);

    // Build redirect to the enter-profile form with original params + client display info
    var enterProfileUrl = BuildEnterProfileRedirect(request, clientInfo);
    return new AuthorizationResult { Success = true, RedirectUrl = enterProfileUrl };
}

// When me IS provided, validate it as before
if (!UrlValidator.IsValidProfileUrl(request.Me))
{
    return ErrorResult("invalid_request",
        "me is not a valid profile URL per the IndieAuth specification");
}

if (!IsProfileHostAllowed(request.Me))
{
    return ErrorResult("access_denied",
        "This server is not configured to authenticate users from the requested website.");
}
```

Add a private helper method:

```csharp
private static string BuildEnterProfileRedirect(AuthorizationRequest request, ClientInfo clientInfo)
{
    var query = new Dictionary<string, string?>
    {
        ["response_type"] = request.ResponseType,
        ["client_id"] = request.ClientId,
        ["redirect_uri"] = request.RedirectUri,
        ["state"] = request.State,
        ["code_challenge"] = request.CodeChallenge,
        ["code_challenge_method"] = request.CodeChallengeMethod,
        ["scope"] = request.Scope,
        ["client_name"] = clientInfo.ClientName,
        ["client_logo"] = clientInfo.LogoUri,
    };

    var queryString = string.Join("&", query
        .Where(kv => kv.Value is not null)
        .Select(kv => $"{kv.Key}={Uri.EscapeDataString(kv.Value!)}"));

    return $"/enter-profile?{queryString}";
}
```

### 2. Add frontend route

**File:** `src/Talos.Web/ClientApp/src/router/index.ts`

```typescript
{
  path: '/enter-profile',
  name: 'enter-profile',
  component: () => import('../views/EnterProfileView.vue'),
}
```

### 3. Add `EnterProfileView.vue`

**File:** `src/Talos.Web/ClientApp/src/views/EnterProfileView.vue` (new)

A simple view that:
- Reads all query params on mount
- Displays client info via `ClientCard` (using `client_name`, `client_logo`, `client_id` from the query)
- Shows a text input for profile URL with placeholder `https://yoursite.example.com/`
- Shows a submit button: "Continue"
- On submit: redirects browser to `/auth?{original_params}&me={entered_profile_url}`
  - "Original params" = all query params minus `client_name` and `client_logo` (those were only for display)
- Shows inline error if the profile URL is obviously empty/invalid (basic frontend-only check; the real validation happens on the re-submitted `/auth` request)

Template structure:

```vue
<template>
  <div class="min-h-screen flex items-center justify-center bg-gray-50 dark:bg-gray-900 p-4">
    <div class="max-w-md w-full space-y-6">
      <ClientCard :client="client" />
      <form @submit.prevent="submit" class="space-y-4">
        <div>
          <label class="block text-sm font-medium ...">Your profile URL</label>
          <input v-model="profileUrl" type="url" placeholder="https://yoursite.example.com/"
                 class="mt-1 block w-full ..." required />
        </div>
        <p v-if="error" class="text-sm text-red-500">{{ error }}</p>
        <button type="submit" class="w-full ...">Continue</button>
      </form>
    </div>
  </div>
</template>
```

Script logic:

```typescript
const route = useRoute()

// Extract client display info from query
const client = computed(() => ({
  clientId: route.query.client_id as string,
  name: route.query.client_name as string | undefined,
  url: route.query.client_id as string,
  logoUrl: route.query.client_logo as string | undefined,
}))

function submit() {
  if (!profileUrl.value) { error.value = 'Please enter your profile URL'; return }

  // Rebuild original auth params (strip display-only params)
  const params = new URLSearchParams()
  for (const [key, value] of Object.entries(route.query)) {
    if (key !== 'client_name' && key !== 'client_logo' && typeof value === 'string') {
      params.set(key, value)
    }
  }
  params.set('me', profileUrl.value)

  window.location.href = `/auth?${params.toString()}`
}
```

**No new API function needed** in `auth.ts` — this is a pure browser redirect.

---

## Tests

### Unit tests: `AuthorizationServiceOptionalMeTests.cs` (new)

| # | Test | Scenario | Expected |
|---|------|----------|----------|
| 1 | `CreateAuthorizationAsync_NoMe_ReturnsEnterProfileRedirect` | Request with empty `me` | `Success=true`, `RedirectUrl` starts with `/enter-profile?` |
| 2 | `CreateAuthorizationAsync_NoMe_RedirectContainsOriginalParams` | Request with empty `me` | Redirect URL contains `client_id`, `redirect_uri`, `state`, `code_challenge`, etc. |
| 3 | `CreateAuthorizationAsync_NoMe_RedirectContainsClientInfo` | Request with empty `me` | Redirect URL contains `client_name` and `client_logo` from discovered client |
| 4 | `CreateAuthorizationAsync_NoMe_FetchesClientInfo` | Request with empty `me` | `IClientDiscoveryService.DiscoverClientAsync` is called |
| 5 | `CreateAuthorizationAsync_NoMe_NoPendingSessionCreated` | Request with empty `me` | `dbContext.PendingAuthentications` count is 0 |
| 6 | `CreateAuthorizationAsync_NoMe_InvalidClientId_StillFailsValidation` | Bad `client_id`, empty `me` | Error returned (client_id validation runs before `me` branch) |
| 7 | `CreateAuthorizationAsync_WithMe_StillWorks` | Request with valid `me` | Same behaviour as before (regression guard) |

### Existing test updates

In `AuthorizationServiceProfileUrlTests.cs`:
- The test `CreateAuthorizationAsync_EmptyMe_ReturnsError` should be **updated** to expect `Success=true` with a redirect to `/enter-profile` instead of an error.
- Tests for **invalid** `me` values (when `me` IS provided) remain unchanged.

---

## What This Approach Avoids

| Eliminated | Why not needed |
|---|---|
| ~~`SetProfileUrlAsync` service method~~ | The existing `CreateAuthorizationAsync` handles the re-submitted request with `me` |
| ~~`POST /api/auth/submit-profile-url` endpoint~~ | Frontend redirects back to `/auth` — no API call |
| ~~`GET /api/auth/session-info` endpoint~~ | Client info is passed via query params from the initial redirect |
| ~~Pending session with empty ProfileUrl~~ | No session created until `me` is present |
| ~~New API function in `auth.ts`~~ | Pure browser redirect, no fetch/axios call |
| ~~Database migration~~ | No schema changes |

## Trade-offs

1. **Slight redundancy:** When the form redirects back, client_id/redirect_uri validation and client info fetch run a second time. This is negligible overhead and keeps the code simple.
2. **Query string size:** All original params + client info are passed in the URL. This is well within browser URL limits (~2KB typical for these params).
3. **Security:** No sensitive data in the query string — these are all values the client originally sent. The `client_name`/`client_logo` are informational only and re-validated on the second pass.

## Risks & Considerations

1. **Rate limiting:** The enter-profile page submits back to `/auth`, which already has the `auth` rate limit policy. No additional rate limiting needed.
2. **AllowedProfileHosts:** When configured, the user might enter a disallowed host. The error will be returned on the second `/auth` call and displayed via the normal error page. Could improve UX later by showing the allowed host as a hint in the form.
3. **Client discovery is idempotent:** Fetching client info twice (once for the form, once on re-submit) is safe — could add caching later if desired.
