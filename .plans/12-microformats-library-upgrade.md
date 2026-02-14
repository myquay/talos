# 12 - Microformats Library Upgrade

## Implementation Status: ✅ COMPLETED (February 15, 2026)

### What Was Implemented
- Added `Microformats` NuGet package (version 1.1.2) for standards-compliant microformats parsing
- Created `IMicroformatsService` interface and `MicroformatsService` implementation
- Created `MicroformatsResult` model for parsed microformats data
- Updated `ProfileDiscoveryService` to use MicroformatsService instead of manual HtmlAgilityPack parsing
- Added 16 comprehensive unit tests for the MicroformatsService
- Removed HtmlAgilityPack package reference (no longer needed)

### Key Implementation Details
- The Microformats library doesn't resolve relative URLs automatically, so URL resolution is handled in the service
- HTTP Link header parsing is kept separate from HTML microformats parsing (as they're different specs)
- The library correctly handles multi-value rel attributes (e.g., `rel="me noopener"`)
- BaseUri must be set via `WithOptions()` configuration

---

## Overview

This plan outlines upgrading Talos to use the `Microformats` NuGet package for parsing and extracting microformats data (such as `rel="me"` links and IndieAuth endpoints) instead of manually parsing HTML with HtmlAgilityPack.

The Microformats library provides a standards-compliant parser that handles edge cases and specification nuances automatically.

### Benefits of Using Microformats Library

1. **Standards Compliance** - Built to parse according to the microformats2 specification
2. **Edge Case Handling** - Properly handles space-separated rel values, case-insensitivity, malformed HTML
3. **Future Proof** - Library updates will automatically include spec changes
4. **Reduced Code** - Less custom parsing logic to maintain
5. **Better rel Parsing** - Correctly handles `rel="me noopener"` and similar multi-value cases
6. **h-card Support** - Opens door to richer profile information extraction in the future

---

## Current State Analysis

### Files with Manual Microformats Extraction

#### 1. `src/Talos.Web/Services/ProfileDiscoveryService.cs`

**Current Implementation:**

```csharp
using HtmlAgilityPack;
```

**Manual Extraction Areas:**

| Method | Line | Description | Microformats Feature |
|--------|------|-------------|---------------------|
| `DiscoverRelMeLinks()` | 67-87 | Extracts `rel="me"` links from `<a>` and `<link>` elements | `rels["me"]` |
| `DiscoverEndpoint()` | 92-115 | Extracts `authorization_endpoint` and `token_endpoint` from `<link>` elements and HTTP headers | `rels["authorization_endpoint"]`, `rels["token_endpoint"]` |

**Issues with Current Implementation:**

1. **XPath selector `[@rel='me']` is exact match only** - Does not handle `rel="me noopener"` or other multi-value cases per specification
2. **Case sensitivity** - Specification allows case-insensitive rel values, but XPath is case-sensitive
3. **No deduplication based on href normalization** - Current deduplication is string-based only
4. **Relative URL resolution** - Handled manually, could have edge cases

#### 2. Package References

**Current in `src/Talos.Web/Talos.Web.csproj`:**
```xml
<PackageReference Include="HtmlAgilityPack" Version="1.12.4" />
```

---

## Proposed Changes

### Phase 1: Add Microformats Package

1. Add the `Microformats` NuGet package to the project
2. Keep HtmlAgilityPack temporarily for any non-microformats HTML parsing needs

### Phase 2: Create Microformats Parsing Service

Create a dedicated service for microformats parsing:

```csharp
// src/Talos.Web/Services/IMicroformatsService.cs
public interface IMicroformatsService
{
    Task<MicroformatsResult> ParseAsync(string html, Uri baseUrl);
}

public class MicroformatsResult
{
    public List<string> RelMeLinks { get; set; } = new();
    public string? AuthorizationEndpoint { get; set; }
    public string? TokenEndpoint { get; set; }
    public string? IndieAuthMetadata { get; set; }
    public string? Micropub { get; set; }
    public string? Microsub { get; set; }
    // Future: h-card data for profile enrichment
}
```

### Phase 3: Update ProfileDiscoveryService

Replace manual HTML parsing with Microformats library calls:

**Before:**
```csharp
var doc = new HtmlDocument();
doc.LoadHtml(html);
var relMeLinks = DiscoverRelMeLinks(doc, result.ProfileUrl);
result.AuthorizationEndpoint = DiscoverEndpoint(doc, response, "authorization_endpoint");
result.TokenEndpoint = DiscoverEndpoint(doc, response, "token_endpoint");
```

**After:**
```csharp
var microformats = await _microformatsService.ParseAsync(html, new Uri(result.ProfileUrl));
var relMeLinks = microformats.RelMeLinks;
result.AuthorizationEndpoint = microformats.AuthorizationEndpoint;
result.TokenEndpoint = microformats.TokenEndpoint;
```

### Phase 4: HTTP Link Header Handling

The Microformats library focuses on HTML parsing. HTTP Link header discovery (`DiscoverEndpoint` method checking response headers) must be preserved or handled separately:

**Options:**
1. Keep HTTP Link header parsing as a separate step (recommended)
2. Create a combined discovery method that merges HTTP headers + HTML rels

**Recommendation:** Keep HTTP Link header parsing separate since it's defined in a different spec (RFC 8288) and is independent of microformats parsing.

### Phase 5: Update Tests

1. Update existing tests in `08-testing.md` to use new service
2. Add tests for edge cases the library now handles:
   - `rel="me noopener"` multi-value parsing
   - Case-insensitive rel matching
   - Malformed HTML tolerance
   - Relative URL resolution edge cases

### Phase 6: Remove HtmlAgilityPack

After migration is complete and tested:
1. Remove HtmlAgilityPack package reference
2. Remove unused using statements

---

## Detailed Implementation

### File Changes

| File | Action | Description |
|------|--------|-------------|
| `Talos.Web.csproj` | Modify | Add Microformats package, eventually remove HtmlAgilityPack |
| `Services/IMicroformatsService.cs` | Create | Interface for microformats parsing |
| `Services/MicroformatsService.cs` | Create | Implementation using Microformats library |
| `Services/ProfileDiscoveryService.cs` | Modify | Replace manual parsing with MicroformatsService |
| `Program.cs` | Modify | Register IMicroformatsService |
| Tests | Modify/Create | Update tests for new implementation |

### New Service Implementation

```csharp
// src/Talos.Web/Services/MicroformatsService.cs
using Microformats;

namespace Talos.Web.Services;

public class MicroformatsService : IMicroformatsService
{
    private readonly ILogger<MicroformatsService> _logger;

    public MicroformatsService(ILogger<MicroformatsService> logger)
    {
        _logger = logger;
    }

    public async Task<MicroformatsResult> ParseAsync(string html, Uri baseUrl)
    {
        var result = new MicroformatsResult();

        try
        {
            var parser = new Mf2();
            var parsed = parser.Parse(html, baseUrl);

            // Extract rel="me" links
            if (parsed.Rels.TryGetValue("me", out var meLinks))
            {
                result.RelMeLinks = meLinks.ToList();
            }

            // Extract IndieAuth endpoints
            if (parsed.Rels.TryGetValue("authorization_endpoint", out var authEndpoints))
            {
                result.AuthorizationEndpoint = authEndpoints.FirstOrDefault();
            }

            if (parsed.Rels.TryGetValue("token_endpoint", out var tokenEndpoints))
            {
                result.TokenEndpoint = tokenEndpoints.FirstOrDefault();
            }

            // Additional IndieWeb endpoints for future use
            if (parsed.Rels.TryGetValue("indieauth-metadata", out var metadataEndpoints))
            {
                result.IndieAuthMetadata = metadataEndpoints.FirstOrDefault();
            }

            if (parsed.Rels.TryGetValue("micropub", out var micropubEndpoints))
            {
                result.Micropub = micropubEndpoints.FirstOrDefault();
            }

            if (parsed.Rels.TryGetValue("microsub", out var microsubEndpoints))
            {
                result.Microsub = microsubEndpoints.FirstOrDefault();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to parse microformats from {BaseUrl}", baseUrl);
        }

        return result;
    }
}
```

### Updated ProfileDiscoveryService

```csharp
public class ProfileDiscoveryService : IProfileDiscoveryService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IIdentityProviderFactory _providerFactory;
    private readonly IMicroformatsService _microformatsService;
    private readonly ILogger<ProfileDiscoveryService> _logger;

    public ProfileDiscoveryService(
        IHttpClientFactory httpClientFactory,
        IIdentityProviderFactory providerFactory,
        IMicroformatsService microformatsService,
        ILogger<ProfileDiscoveryService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _providerFactory = providerFactory;
        _microformatsService = microformatsService;
        _logger = logger;
    }

    public async Task<ProfileDiscoveryResult> DiscoverProfileAsync(string profileUrl)
    {
        var result = new ProfileDiscoveryResult
        {
            ProfileUrl = NormalizeProfileUrl(profileUrl)
        };

        try
        {
            var client = _httpClientFactory.CreateClient("ProfileDiscovery");
            var response = await client.GetAsync(result.ProfileUrl);
            
            if (!response.IsSuccessStatusCode)
            {
                result.Error = $"Failed to fetch profile: {response.StatusCode}";
                return result;
            }

            var html = await response.Content.ReadAsStringAsync();

            // Parse microformats using library
            var microformats = await _microformatsService.ParseAsync(html, new Uri(result.ProfileUrl));
            
            // Discover rel="me" links
            var relMeLinks = microformats.RelMeLinks;
            
            // Match links against supported identity providers
            foreach (var link in relMeLinks)
            {
                var provider = _providerFactory.GetProviderForUrl(link);
                if (provider != null)
                {
                    result.Providers.Add(new DiscoveredProvider
                    {
                        Type = provider.ProviderType,
                        Name = provider.DisplayName,
                        ProfileUrl = link
                    });
                }
            }

            // Discover IndieAuth endpoints (check HTTP headers first, then HTML)
            result.AuthorizationEndpoint = DiscoverEndpointFromHeaders(response, "authorization_endpoint") 
                                          ?? microformats.AuthorizationEndpoint;
            result.TokenEndpoint = DiscoverEndpointFromHeaders(response, "token_endpoint") 
                                  ?? microformats.TokenEndpoint;

            result.Success = true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error discovering profile for {ProfileUrl}", profileUrl);
            result.Error = "An error occurred while discovering your profile.";
        }

        return result;
    }

    private static string? DiscoverEndpointFromHeaders(HttpResponseMessage response, string rel)
    {
        // Check HTTP Link header (RFC 8288)
        if (response.Headers.TryGetValues("Link", out var linkHeaders))
        {
            foreach (var header in linkHeaders)
            {
                var match = System.Text.RegularExpressions.Regex.Match(
                    header, 
                    $"""<([^>]+)>;\s*rel="?{rel}"?""");
                if (match.Success)
                {
                    return match.Groups[1].Value;
                }
            }
        }
        return null;
    }

    // ... rest of methods
}
```

---

## Testing Strategy

### Unit Tests to Update

1. **RelMeDiscoveryTests** - Update to test through MicroformatsService
2. **ProfileDiscoveryServiceTests** - Mock IMicroformatsService

### New Test Cases to Add

```csharp
// tests/Talos.Web.Tests/Services/MicroformatsServiceTests.cs
public class MicroformatsServiceTests
{
    [Fact]
    public async Task ParseAsync_RelMeWithMultipleValues_ExtractsLink()
    {
        // Handles rel="me noopener"
        var html = @"<a href=""https://github.com/user"" rel=""me noopener"">GitHub</a>";
        // ...
    }

    [Fact]
    public async Task ParseAsync_CaseInsensitiveRel_ExtractsLink()
    {
        // Handles rel="ME" and rel="Me"
        var html = @"<a href=""https://github.com/user"" rel=""ME"">GitHub</a>";
        // ...
    }

    [Fact]
    public async Task ParseAsync_RelativeUrls_ResolvesCorrectly()
    {
        var html = @"<link rel=""authorization_endpoint"" href=""/auth"">";
        var baseUrl = new Uri("https://example.com/page");
        // Should resolve to https://example.com/auth
    }

    [Fact]
    public async Task ParseAsync_MultipleEndpoints_ReturnsFirst()
    {
        // Per IndieAuth spec, use first discovered endpoint
        var html = @"
            <link rel=""authorization_endpoint"" href=""https://auth1.example.com"">
            <link rel=""authorization_endpoint"" href=""https://auth2.example.com"">";
        // Should return auth1
    }

    [Fact]
    public async Task ParseAsync_AllIndieWebEndpoints_ExtractsAll()
    {
        var html = @"
            <link rel=""authorization_endpoint"" href=""https://example.com/auth"">
            <link rel=""token_endpoint"" href=""https://example.com/token"">
            <link rel=""indieauth-metadata"" href=""https://example.com/.well-known/oauth-authorization-server"">
            <link rel=""micropub"" href=""https://example.com/micropub"">
            <link rel=""microsub"" href=""https://example.com/microsub"">";
        // Should extract all endpoints
    }
}
```

---

## Implementation Checklist

### Phase 1: Package Setup
- [x] Research and verify the exact NuGet package name (likely `Microformats` or `Microformats.NET`)
- [x] Add Microformats NuGet package to `Talos.Web.csproj`
- [x] Verify package installation and compatibility

### Phase 2: Create Microformats Service
- [x] Create `IMicroformatsService.cs` interface
- [x] Create `MicroformatsResult.cs` model class
- [x] Create `MicroformatsService.cs` implementation
- [x] Register service in `Program.cs`

### Phase 3: Update ProfileDiscoveryService
- [x] Add `IMicroformatsService` dependency injection
- [x] Replace `DiscoverRelMeLinks()` with microformats service call
- [x] Update endpoint discovery to use microformats service + HTTP headers
- [x] Remove private `DiscoverRelMeLinks()` method
- [x] Remove private `DiscoverEndpoint()` method (keep HTTP header part as separate method)
- [x] Remove `using HtmlAgilityPack;` import
- [x] Remove HtmlDocument usage

### Phase 4: Update Tests
- [x] Create `MicroformatsServiceTests.cs` with comprehensive tests
- [x] Update `ProfileDiscoveryServiceTests` to mock IMicroformatsService
- [x] Add edge case tests for multi-value rel attributes
- [x] Add case-insensitivity tests
- [x] Add relative URL resolution tests
- [x] Run all tests and verify passing

### Phase 5: Cleanup
- [x] Remove HtmlAgilityPack package reference from `Talos.Web.csproj`
- [x] Run `dotnet build` to verify no compilation errors
- [x] Run all tests

### Phase 6: Documentation
- [x] Update `12-microformats-library-upgrade.md` plan with implementation notes
- [ ] Update `02-profile-discovery.md` plan with implementation notes (if needed)
- [ ] Update README if any user-facing behavior changes

---

## Future Considerations

### h-card Extraction

The Microformats library also supports parsing h-card (representative profile information). This could be used to:

1. **Display user's name** instead of just URL during consent
2. **Extract user's photo** for a richer UI
3. **Pre-fill profile data** for IndieAuth token responses

Example future enhancement:
```csharp
// In MicroformatsResult
public HCard? RepresentativeHCard { get; set; }

public class HCard
{
    public string? Name { get; set; }
    public string? Photo { get; set; }
    public string? Url { get; set; }
    public string? Email { get; set; }
}
```

### h-x-app Extraction

For client application discovery, the library could parse h-x-app for:
- Client name
- Client logo
- Client URL

This would enable automatic client metadata discovery without requiring manual registration.

---

## Risk Assessment

| Risk | Likelihood | Impact | Mitigation |
|------|------------|--------|------------|
| Package API differences | Medium | Medium | Review package documentation thoroughly before implementation |
| Performance impact | Low | Low | Library parsing should be comparable; benchmark if concerned |
| Edge case regressions | Medium | Medium | Comprehensive test suite before migration |
| Package maintenance | Low | Medium | Microformats is a well-established spec with active community |

---

## Implementation Notes

### Package Details
- **Package**: `Microformats` version 1.1.2 from NuGet
- **API Usage**: `new Mf2().WithOptions(o => { o.BaseUri = baseUrl; return o; }).Parse(html)`
- **Result Structure**: `MfResult` with `Rels` dictionary containing `string[]` for each rel type

### Key Findings During Implementation

1. **BaseUri Configuration**: The library's `BaseUri` option must be set via `WithOptions()`, not as a parameter to `Parse()`.

2. **Relative URL Resolution**: The Microformats library does NOT resolve relative URLs automatically. URLs in `Rels` dictionary remain as-is from the HTML. Our `MicroformatsService` handles resolution using `Uri.TryCreate(baseUrl, relativeUrl, out var resolved)`.

3. **Multi-value rel Handling**: The library correctly parses `rel="me noopener"` and adds the URL to both the `me` and `noopener` keys in the `Rels` dictionary.

4. **URL Scheme Detection**: When resolving URLs, we check for `http` or `https` schemes explicitly to avoid treating paths like `/auth` as absolute `file:///auth` URLs.

### Files Created
- `src/Talos.Web/Services/IMicroformatsService.cs` - Interface for microformats parsing
- `src/Talos.Web/Services/MicroformatsResult.cs` - Result model class
- `src/Talos.Web/Services/MicroformatsService.cs` - Implementation using Microformats library
- `tests/Talos.Web.Tests/Services/MicroformatsServiceTests.cs` - 16 unit tests

### Files Modified
- `src/Talos.Web/Program.cs` - Registered `MicroformatsService` as singleton
- `src/Talos.Web/Services/ProfileDiscoveryService.cs` - Replaced manual HtmlAgilityPack parsing with MicroformatsService
- `src/Talos.Web/Talos.Web.csproj` - Added Microformats package, removed HtmlAgilityPack

### Test Coverage
All 16 new tests pass covering:
- Basic rel="me" link extraction from `<link>` and `<a>` elements
- Multi-value rel attributes (`rel="me noopener"`)
- Deduplication of duplicate links
- Relative URL resolution
- IndieAuth endpoint extraction (authorization_endpoint, token_endpoint, etc.)
- Edge cases (empty HTML, malformed HTML, empty href)

### Backwards Compatibility
The changes are fully backwards compatible. The same `ProfileDiscoveryService` interface is maintained, only the internal implementation changed from HtmlAgilityPack XPath queries to the Microformats library.

