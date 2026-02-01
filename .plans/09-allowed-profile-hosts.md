# 09 - Allowed Profile Hosts

## Overview

Add an optional configuration to restrict which website hosts can authenticate with Talos. This enables personal server deployments where only specific websites (e.g., the owner's personal site) can use the service.

**Default Behavior:** All hosts are allowed (current behavior preserved)  
**When Configured:** Only websites with hosts matching the allowlist can authenticate

## Use Case

A user running a personal Talos instance wants to ensure only their own website(s) can authenticate through it, preventing others from using their server as a shared IndieAuth provider.

## Design Decisions

| Decision | Choice | Rationale |
|----------|--------|-----------|
| Host matching | Exact match only | Simplicity; no wildcard/subdomain support initially |
| Error messaging | Generic error without host reflection | Security; don't reveal which hosts are allowed |
| Property name | `AllowedProfileHosts` | Clear, descriptive, follows existing naming conventions |
| Default value | `null` (empty array = all allowed) | Backward compatible; existing deployments unaffected |

---

## Implementation Phases

### Phase 1: Configuration Model

**Files to modify:**
- `src/Talos.Web/Configuration/TalosSettings.cs`

**Changes:**
- Add `AllowedProfileHosts` property as `string[]?` defaulting to `null`

**Example:**
```csharp
public class TalosSettings
{
    public string BaseUrl { get; set; } = "";
    public string[]? AllowedProfileHosts { get; set; }
}
```

### Phase 2: Validation Logic

**Files to modify:**
- `src/Talos.Web/Services/AuthorizationService.cs`

**Changes:**
- Add private method `IsProfileHostAllowed(string profileUrl)` that:
  1. Returns `true` if `AllowedProfileHosts` is null or empty
  2. Parses the `profileUrl` to extract the host
  3. Performs case-insensitive exact match against allowed hosts
  4. Returns `true` if host matches, `false` otherwise

- In `CreateAuthorizationAsync()`, after validating `me` parameter is present but before calling `profileDiscovery.DiscoverProfileAsync()`:
  1. Call `IsProfileHostAllowed(request.Me)`
  2. If `false`, return error result with:
     - Error code: `"access_denied"`
     - Description: `"This server is not configured to authenticate users from the requested website."`

**Example:**
```csharp
// After validating 'me' parameter exists
if (!IsProfileHostAllowed(request.Me))
{
    return ErrorResult("access_denied", 
        "This server is not configured to authenticate users from the requested website.");
}
```

### Phase 3: Configuration File Updates

**Files to modify:**
- `src/Talos.Web/appsettings.example.json`

**Changes:**
- Add `AllowedProfileHosts` to the `Talos` section with a commented explanation

**Example:**
```json
"Talos": {
  "BaseUrl": "https://talos.example.com",
  "AllowedProfileHosts": null
}
```

### Phase 4: Documentation Updates

#### 4.1 README.md

**File:** `README.md`

**Changes:**
- Add new section "Personal Server Mode" or similar under Configuration
- Document the `AllowedProfileHosts` setting with examples
- Explain use case for restricting to personal website

**Content to add:**
```markdown
### Personal Server Mode

To restrict Talos to only authenticate users from specific websites (useful for personal servers):

```json
"Talos": {
  "BaseUrl": "https://auth.example.com",
  "AllowedProfileHosts": ["jane.example.com", "blog.jane.example.com"]
}
```

When configured, only users whose `me` URL matches one of the allowed hosts can authenticate. Leave as `null` or omit to allow all hosts (default behavior).
```

#### 4.2 ProjectInfo.vue

**File:** `src/Talos.Web/ClientApp/src/components/ProjectInfo.vue`

**Changes:**
- Add a note in the "Configure Your Website" section or add a new "Personal Server" section
- Mention that server operators can restrict which websites can authenticate

**Content to add:**
- New callout box or section explaining the restriction capability for self-hosted deployments

#### 4.3 SetupGuide.vue

**File:** `src/Talos.Web/ClientApp/src/components/SetupGuide.vue`

**Changes:**
- Update the `getConfigExample()` function to include `AllowedProfileHosts` in the Talos section
- Update `getCategoryHelp()` to mention the optional host restriction

**Updated Talos config example:**
```typescript
Talos: `"Talos": {
  "BaseUrl": "https://your-domain.com",
  "AllowedProfileHosts": null
}`
```

**Updated Talos help:**
```typescript
Talos: `
  <ol class="list-decimal list-inside space-y-1">
    <li>Set BaseUrl to the public URL where Talos is hosted</li>
    <li>Include the protocol (https://) but no trailing slash</li>
    <li>This URL is used for OAuth callbacks and token issuing</li>
    <li>Optionally set AllowedProfileHosts to restrict which websites can authenticate</li>
  </ol>
`
```

### Phase 5: Unit Tests

**Files to create/modify:**
- `tests/Talos.Web.Tests/Services/AuthorizationServiceTests.cs` (create if needed)

**Test Cases:**

1. **AllowedProfileHosts is null - all hosts allowed**
   - Configure `TalosSettings.AllowedProfileHosts = null`
   - Call `CreateAuthorizationAsync` with any valid `me` URL
   - Assert: Should proceed to profile discovery (not rejected)

2. **AllowedProfileHosts is empty array - all hosts allowed**
   - Configure `TalosSettings.AllowedProfileHosts = []`
   - Call `CreateAuthorizationAsync` with any valid `me` URL
   - Assert: Should proceed to profile discovery (not rejected)

3. **AllowedProfileHosts configured - matching host allowed**
   - Configure `TalosSettings.AllowedProfileHosts = ["example.com"]`
   - Call `CreateAuthorizationAsync` with `me = "https://example.com/"`
   - Assert: Should proceed to profile discovery (not rejected)

4. **AllowedProfileHosts configured - non-matching host denied**
   - Configure `TalosSettings.AllowedProfileHosts = ["example.com"]`
   - Call `CreateAuthorizationAsync` with `me = "https://other.com/"`
   - Assert: Should return error with code `"access_denied"`

5. **AllowedProfileHosts - case insensitive matching**
   - Configure `TalosSettings.AllowedProfileHosts = ["Example.COM"]`
   - Call `CreateAuthorizationAsync` with `me = "https://example.com/"`
   - Assert: Should proceed (case insensitive match)

6. **AllowedProfileHosts - subdomain not matched by parent domain**
   - Configure `TalosSettings.AllowedProfileHosts = ["example.com"]`
   - Call `CreateAuthorizationAsync` with `me = "https://blog.example.com/"`
   - Assert: Should return error (exact match only)

7. **AllowedProfileHosts - multiple hosts, one matches**
   - Configure `TalosSettings.AllowedProfileHosts = ["foo.com", "bar.com", "example.com"]`
   - Call `CreateAuthorizationAsync` with `me = "https://bar.com/user"`
   - Assert: Should proceed (matches one of the allowed hosts)

---

## Implementation Checklist

### Phase 1: Configuration Model
- [x] Update `TalosSettings.cs` to add `AllowedProfileHosts` property

### Phase 2: Validation Logic
- [x] Add `IsProfileHostAllowed()` helper method to `AuthorizationService.cs`
- [x] Add host validation check in `CreateAuthorizationAsync()` method
- [x] Verify error response format matches OAuth 2.0 error response

### Phase 3: Configuration Files
- [x] Update `appsettings.example.json` with new setting

### Phase 4: Documentation
- [x] Update `README.md` with Personal Server Mode section
- [x] Update `ProjectInfo.vue` with restriction note
- [x] Update `SetupGuide.vue` config example and help text

### Phase 5: Testing
- [x] Create `AuthorizationServiceAllowedHostsTests.cs`
- [x] Implement test: null AllowedProfileHosts allows all
- [x] Implement test: empty array allows all
- [x] Implement test: matching host allowed
- [x] Implement test: non-matching host denied
- [x] Implement test: case insensitive matching
- [x] Implement test: subdomain exact match only
- [x] Implement test: multiple hosts, one matches
- [x] Implement test: invalid URL is denied
- [x] Implement test: error message does not reveal hosts
- [x] Run all tests and verify passing (88/88 tests pass)

### Phase 6: Final Verification
- [x] Build solution without errors
- [x] All tests passing
- [x] Review error messages for security (no host reflection)

---

## Implementation Notes (February 1, 2026)

### Files Modified

1. **`src/Talos.Web/Configuration/TalosSettings.cs`**
   - Added `AllowedProfileHosts` property with XML documentation

2. **`src/Talos.Web/Services/AuthorizationService.cs`**
   - Added `IsProfileHostAllowed(string profileUrl)` private method
   - Added validation check after `me` parameter validation, before profile discovery

3. **`src/Talos.Web/appsettings.example.json`**
   - Added `AllowedProfileHosts: null` to Talos section

4. **`README.md`**
   - Added "Personal Server Mode" subsection under Configuration

5. **`src/Talos.Web/ClientApp/src/components/ProjectInfo.vue`**
   - Added self-hosted tip callout box in Configure Your Website section

6. **`src/Talos.Web/ClientApp/src/components/SetupGuide.vue`**
   - Updated Talos config example to include `AllowedProfileHosts`
   - Updated Talos help text to mention the optional restriction

### Files Created

1. **`tests/Talos.Web.Tests/Services/AuthorizationServiceAllowedHostsTests.cs`**
   - 9 test cases covering all specified scenarios plus edge cases

### Dependencies Added

- `Microsoft.EntityFrameworkCore.InMemory` v10.0.2 added to test project for database mocking

---

## Error Response Format

When a host is denied, the error follows OAuth 2.0 conventions:

```json
{
  "error": "access_denied",
  "error_description": "This server is not configured to authenticate users from the requested website."
}
```

The error message intentionally does NOT include:
- The attempted host
- The list of allowed hosts
- Any indication of what hosts would be accepted

---

## Configuration Examples

### Personal Single-Site Server
```json
"Talos": {
  "BaseUrl": "https://auth.jane.dev",
  "AllowedProfileHosts": ["jane.dev"]
}
```

### Personal Multi-Site Server
```json
"Talos": {
  "BaseUrl": "https://auth.jane.dev",
  "AllowedProfileHosts": ["jane.dev", "blog.jane.dev", "projects.jane.dev"]
}
```

### Public Server (Default)
```json
"Talos": {
  "BaseUrl": "https://talos.example.com",
  "AllowedProfileHosts": null
}
```

Or simply omit the property:
```json
"Talos": {
  "BaseUrl": "https://talos.example.com"
}
```

---

## Future Considerations

These are NOT part of this implementation but could be added later:

1. **Wildcard subdomain support** - e.g., `*.example.com` to match all subdomains
2. **Regex pattern matching** - For complex host matching requirements
3. **Blocklist mode** - Deny specific hosts instead of allowlist
4. **Runtime configuration** - Admin UI to manage allowed hosts without restart
5. **Logging** - Optional logging of denied authentication attempts (without PII)

