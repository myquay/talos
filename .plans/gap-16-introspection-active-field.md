# Fix Plan: GAP-16 — Fix introspection `active` field type

**Status:** ✅ Complete
**Spec requirement:** INTRO-4, INTRO-5 (§6.2)
**Severity:** P0 — Critical (interoperability)
**Completed:** All production code and tests implemented. 173/173 tests passing.

---

## Problem

The introspection endpoint in [TokenController.cs](../src/Talos.Web/Controllers/TokenController.cs#L201) uses anonymous objects for its JSON responses:

```csharp
return Ok(new { active = false });
// and
return Ok(new
{
    active = true,
    me = result.ProfileUrl,
    client_id = result.ClientId,
    scope = string.Join(" ", result.Scopes),
    exp = new DateTimeOffset(result.ExpiresAt!.Value).ToUnixTimeSeconds()
});
```

While the C# `bool` values `true`/`false` *currently* serialize as JSON booleans with the default `System.Text.Json` configuration, there are several concerns:

1. **No compile-time contract** — anonymous objects provide no structural guarantee. A refactor (e.g., wrapping in a DTO with a `string Active` property, or a naming policy change) could silently break the JSON contract.

2. **No test coverage** — there is no test that verifies the wire format. The `active` field being boolean is a MUST-level requirement per RFC 7662 §2.2 and IndieAuth §6.2. A regression here would cause all resource servers to reject valid tokens (they check `if response["active"] == true`).

3. **Missing `iat` claim** — the active response should include `iat` (issued at) per RFC 7662.

4. **Property naming fragility** — properties like `client_id` rely on the anonymous object member name matching the JSON key. If ASP.NET Core's `PropertyNamingPolicy` ever changes or a custom policy is added, underscore-separated names could break.

Per IndieAuth spec §6.2:
> The introspection response MUST include an `active` property as a **boolean**.

Per RFC 7662 §2.2:
> `active` REQUIRED. Boolean indicator of whether or not the presented token is currently active.

## Fix Plan

### Step 1: Create a strongly-typed `IntrospectionResponse` class

**File:** `src/Talos.Web/Controllers/TokenController.cs` (alongside existing request/response classes at bottom of file)

```csharp
/// <summary>
/// Token introspection response per RFC 7662 §2.2 and IndieAuth §6.2.
/// </summary>
public class IntrospectionResponse
{
    /// <summary>
    /// Boolean indicator of whether or not the presented token is currently active.
    /// MUST be a JSON boolean — never a string.
    /// </summary>
    [JsonPropertyName("active")]
    public bool Active { get; set; }

    [JsonPropertyName("me")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Me { get; set; }

    [JsonPropertyName("client_id")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ClientId { get; set; }

    [JsonPropertyName("scope")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Scope { get; set; }

    [JsonPropertyName("exp")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public long Exp { get; set; }

    [JsonPropertyName("iat")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public long Iat { get; set; }
}
```

Key design decisions:
- `[JsonPropertyName]` attributes ensure property names are correct regardless of any `PropertyNamingPolicy` configured on the app.
- `[JsonIgnore(Condition = ...)]` omits null/default fields from the inactive response (which should contain only `{ "active": false }`).
- `Active` is explicitly typed as `bool`, making it impossible to accidentally serialize as a string.

### Step 2: Replace anonymous objects in the `Introspect` action

**File:** `src/Talos.Web/Controllers/TokenController.cs`

Change from:
```csharp
[HttpPost("introspect")]
[Consumes("application/x-www-form-urlencoded")]
public async Task<IActionResult> Introspect([FromForm] string token)
{
    if (string.IsNullOrEmpty(token))
    {
        return Ok(new { active = false });
    }

    var result = await tokenService.ValidateAccessTokenAsync(token);
    
    if (!result.IsValid)
    {
        return Ok(new { active = false });
    }

    return Ok(new
    {
        active = true,
        me = result.ProfileUrl,
        client_id = result.ClientId,
        scope = string.Join(" ", result.Scopes),
        exp = new DateTimeOffset(result.ExpiresAt!.Value).ToUnixTimeSeconds()
    });
}
```

To:
```csharp
[HttpPost("introspect")]
[Consumes("application/x-www-form-urlencoded")]
public async Task<IActionResult> Introspect([FromForm] string token)
{
    if (string.IsNullOrEmpty(token))
    {
        return Ok(new IntrospectionResponse { Active = false });
    }

    var result = await tokenService.ValidateAccessTokenAsync(token);
    
    if (!result.IsValid)
    {
        return Ok(new IntrospectionResponse { Active = false });
    }

    return Ok(new IntrospectionResponse
    {
        Active = true,
        Me = result.ProfileUrl,
        ClientId = result.ClientId,
        Scope = string.Join(" ", result.Scopes),
        Exp = new DateTimeOffset(result.ExpiresAt!.Value).ToUnixTimeSeconds(),
        Iat = new DateTimeOffset(result.IssuedAt!.Value).ToUnixTimeSeconds()
    });
}
```

**Note on `iat`:** The `TokenValidationResult` class may not currently have an `IssuedAt` property. If not, this can be added by extracting the `iat` claim from the JWT during validation in `TokenService.ValidateAccessTokenAsync`. Alternatively, `iat` can be omitted from v1 and added later — it's a SHOULD, not MUST, per RFC 7662.

### Step 3: Add `using` directive for `System.Text.Json.Serialization`

**File:** `src/Talos.Web/Controllers/TokenController.cs`

Add at the top:
```csharp
using System.Text.Json.Serialization;
```

---

## Unit Tests

### File: `tests/Talos.Web.Tests/Services/IntrospectionResponseTests.cs`

These tests verify the JSON serialization contract directly — the most important aspect of this fix:

```csharp
// === JSON serialization correctness ===

[Fact] InactiveResponse_ActiveIsBooleanFalse
  // Serialize IntrospectionResponse { Active = false } to JSON
  // Parse the JSON string manually (or with JsonDocument)
  // Assert: the "active" element is JsonValueKind.False (not a string)

[Fact] InactiveResponse_ContainsOnlyActive
  // Serialize IntrospectionResponse { Active = false } to JSON
  // Assert: JSON object has exactly one property: "active"
  // (null/default fields like me, client_id, scope, exp should be omitted)

[Fact] ActiveResponse_ActiveIsBooleanTrue
  // Serialize a full IntrospectionResponse { Active = true, Me = "...", ... }
  // Assert: the "active" element is JsonValueKind.True

[Fact] ActiveResponse_ContainsAllRequiredFields
  // Serialize a full IntrospectionResponse
  // Assert: JSON contains "active", "me", "client_id", "scope", "exp"
  // Assert: all property names use snake_case (e.g., "client_id", not "clientId")

[Fact] ActiveResponse_PropertyNamesAreCorrect
  // Serialize and verify exact property names:
  //   "active" (not "Active")
  //   "me" (not "Me")
  //   "client_id" (not "clientId" or "ClientId")
  //   "scope" (not "Scope")
  //   "exp" (not "Exp")

[Fact] ActiveResponse_ExpIsUnixTimestamp
  // Set Exp to a known DateTimeOffset and verify the JSON value
  // is the correct Unix epoch seconds (integer, not string)
```

### File: `tests/Talos.Web.Tests/Controllers/TokenControllerIntrospectionTests.cs`

These tests verify the controller action wiring (optional, higher-level):

```csharp
// === Controller-level introspection tests ===

[Fact] Introspect_EmptyToken_ReturnsInactiveResponse
  // Act: call Introspect("") 
  // Assert: OkObjectResult with IntrospectionResponse { Active = false }

[Fact] Introspect_InvalidToken_ReturnsInactiveResponse
  // Setup: mock tokenService.ValidateAccessTokenAsync returns IsValid = false
  // Assert: OkObjectResult with Active = false

[Fact] Introspect_ValidToken_ReturnsActiveResponse
  // Setup: mock tokenService.ValidateAccessTokenAsync returns IsValid = true
  //   with ProfileUrl, ClientId, Scopes, ExpiresAt
  // Assert: OkObjectResult with Active = true, correct me/client_id/scope/exp
```

---

## Files Modified

| File | Change |
|------|--------|
| `src/Talos.Web/Controllers/TokenController.cs` | Add `IntrospectionResponse` class with `[JsonPropertyName]` attributes; replace anonymous objects in `Introspect` action; add `using System.Text.Json.Serialization` |
| `tests/.../Services/IntrospectionResponseTests.cs` | New test file — ~6 tests for JSON serialization correctness |
| `tests/.../Controllers/TokenControllerIntrospectionTests.cs` | New test file — ~3 controller-level tests (optional) |

## Dependencies

- If `iat` is desired in the response, `TokenValidationResult` needs an `IssuedAt` property and `TokenService.ValidateAccessTokenAsync` needs to extract the `iat` claim from the JWT. This is a minor addition but can be deferred — `iat` is SHOULD-level per RFC 7662, not MUST.

## Out of Scope

- Adding authorization to the introspection endpoint (GAP-7 / fix 1.4 — separate plan)
- Adding `token_type` or `sub` fields (optional per RFC 7662)
