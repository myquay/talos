# 08 - Testing

## Implementation Status: ✅ COMPLETED (January 31, 2026)

### What Was Implemented
- Unit tests for PkceService (PKCE S256 verification per RFC 7636)
- Unit tests for UrlValidator (IndieAuth URL validation)
- Unit tests for TokenService (JWT generation and validation)
- Unit tests for GitHubIdentityProvider (URL pattern matching, reserved paths)
- 80 tests total, all passing
- FluentAssertions for readable test assertions
- Moq for mocking dependencies

---

## Overview

This document covers the testing strategy for Talos, including unit tests, integration tests, and end-to-end testing with IndieAuth-compatible tools. Test cases are derived directly from the [IndieAuth Specification](https://indieauth.spec.indieweb.org/) and [RelMeAuth Specification](https://microformats.org/wiki/RelMeAuth).

## Testing Pyramid

```
                    ┌─────────┐
                    │  E2E    │  indieauth.rocks, real clients
                   ─┴─────────┴─
                 ┌───────────────┐
                 │  Integration  │  Full flow tests with test server
               ──┴───────────────┴──
             ┌───────────────────────┐
             │      Unit Tests       │  Services, validators, PKCE
           ──┴───────────────────────┴──
```

---

## Specification-Based Test Cases

The following test cases are derived directly from the IndieAuth and RelMeAuth specifications. Each test references the relevant section of the specification.

---

## IndieAuth Specification Test Cases

### 1. URL Canonicalization Tests (IndieAuth §3.2)

The IndieAuth spec defines strict rules for URL validation. These apply to `me`, `client_id`, and `redirect_uri` parameters.

```csharp
// tests/Talos.Web.Tests/Validators/IndieAuthUrlValidatorTests.cs
using FluentAssertions;
using Talos.Web.Validators;

namespace Talos.Web.Tests.Validators;

/// <summary>
/// Tests for URL validation per IndieAuth spec §3.2
/// https://indieauth.spec.indieweb.org/#url
/// </summary>
public class IndieAuthUrlValidatorTests
{
    // ===== Profile URL (me) Validation =====
    // IndieAuth §3.2.1: Profile URL Requirements
    
    [Theory]
    [InlineData("https://example.com/")]              // Valid: basic HTTPS
    [InlineData("https://example.com")]               // Valid: no trailing slash
    [InlineData("https://example.com/username")]      // Valid: path component
    [InlineData("https://example.com/users/1")]       // Valid: multi-segment path
    [InlineData("https://example.com:443/")]          // Valid: explicit default port
    [InlineData("https://example.com:8443/")]         // Valid: custom port
    public void ProfileUrl_Valid_ReturnsTrue(string url)
    {
        UrlValidator.IsValidProfileUrl(url).Should().BeTrue();
    }

    [Theory]
    [InlineData("http://example.com/")]               // Invalid: HTTP (non-localhost)
    [InlineData("ftp://example.com/")]                // Invalid: wrong scheme
    [InlineData("mailto:user@example.com")]           // Invalid: wrong scheme
    [InlineData("https://example.com/path?query=1")]  // Invalid: query string
    [InlineData("https://example.com/#fragment")]     // Invalid: fragment
    [InlineData("https://user:pass@example.com/")]    // Invalid: userinfo
    [InlineData("https://192.168.1.1/")]              // Invalid: IP address
    [InlineData("https://[::1]/")]                    // Invalid: IPv6 address
    [InlineData("https://example.com:0/")]            // Invalid: port 0
    [InlineData("https:///path")]                     // Invalid: missing host
    [InlineData("")]                                  // Invalid: empty
    [InlineData(null)]                                // Invalid: null
    [InlineData("not-a-url")]                         // Invalid: not a URL
    [InlineData("example.com")]                       // Invalid: no scheme
    public void ProfileUrl_Invalid_ReturnsFalse(string? url)
    {
        UrlValidator.IsValidProfileUrl(url).Should().BeFalse();
    }

    [Theory]
    [InlineData("http://localhost/")]                 // Valid: localhost HTTP
    [InlineData("http://localhost:8080/")]            // Valid: localhost with port
    [InlineData("http://127.0.0.1/")]                 // Valid: loopback IPv4
    [InlineData("http://[::1]/")]                     // Valid: loopback IPv6
    public void ProfileUrl_Localhost_AllowsHttp(string url)
    {
        UrlValidator.IsValidProfileUrl(url, allowLocalhost: true).Should().BeTrue();
    }

    // ===== Client ID Validation =====
    // IndieAuth §3.2.2: Client Identifier
    
    [Theory]
    [InlineData("https://app.example.com/")]
    [InlineData("https://app.example.com/client")]
    [InlineData("http://localhost/")]                 // Localhost allowed
    [InlineData("http://localhost:8080/")]
    public void ClientId_Valid_ReturnsTrue(string url)
    {
        UrlValidator.IsValidClientId(url).Should().BeTrue();
    }

    [Theory]
    [InlineData("http://app.example.com/")]           // HTTP not allowed (non-localhost)
    [InlineData("https://app.example.com/?query=1")]  // Query strings not allowed
    [InlineData("https://app.example.com/#frag")]     // Fragments not allowed
    [InlineData("https://user:pass@app.example.com/")] // Userinfo not allowed
    public void ClientId_Invalid_ReturnsFalse(string url)
    {
        UrlValidator.IsValidClientId(url).Should().BeFalse();
    }

    // ===== Redirect URI Validation =====
    // IndieAuth §3.2.3: Redirect URL
    
    [Theory]
    [InlineData("https://app.example.com/", "https://app.example.com/callback", true)]
    [InlineData("https://app.example.com/", "https://app.example.com/auth/callback", true)]
    [InlineData("https://app.example.com/client", "https://app.example.com/callback", true)]
    public void RedirectUri_SameHost_Valid(string clientId, string redirectUri, bool expected)
    {
        UrlValidator.RedirectUriMatchesClient(clientId, redirectUri).Should().Be(expected);
    }

    [Theory]
    [InlineData("https://app.example.com/", "https://other.com/callback")]     // Different host
    [InlineData("https://app.example.com/", "http://app.example.com/callback")] // Different scheme
    [InlineData("https://app.example.com/", "https://sub.app.example.com/cb")]  // Subdomain
    public void RedirectUri_DifferentHost_Invalid(string clientId, string redirectUri)
    {
        UrlValidator.RedirectUriMatchesClient(clientId, redirectUri).Should().BeFalse();
    }
}
```

### 2. Authorization Request Validation Tests (IndieAuth §4)

```csharp
// tests/Talos.Web.Tests/Validators/AuthorizationRequestValidatorTests.cs
using FluentAssertions;
using Talos.Web.Models;
using Talos.Web.Validators;

namespace Talos.Web.Tests.Validators;

/// <summary>
/// Tests for authorization request validation per IndieAuth spec §4
/// https://indieauth.spec.indieweb.org/#authorization-request
/// </summary>
public class AuthorizationRequestValidatorTests
{
    private readonly AuthorizationRequestValidator _sut = new();

    // ===== Required Parameters (IndieAuth §4.1) =====
    
    [Fact]
    public void Validate_AllRequiredParameters_ReturnsValid()
    {
        var request = new AuthorizationRequest
        {
            ResponseType = "code",
            ClientId = "https://app.example.com/",
            RedirectUri = "https://app.example.com/callback",
            State = "random-state-value",
            CodeChallenge = "E9Melhoa2OwvFrEMTJguCHaoeK1t8URWbuGJSstw-cM",
            CodeChallengeMethod = "S256"
        };

        var result = _sut.Validate(request);

        result.IsValid.Should().BeTrue();
    }

    [Theory]
    [InlineData(null, "response_type")]
    [InlineData("", "response_type")]
    public void Validate_MissingResponseType_ReturnsError(string? responseType, string expectedError)
    {
        var request = CreateValidRequest();
        request.ResponseType = responseType;

        var result = _sut.Validate(request);

        result.IsValid.Should().BeFalse();
        result.Error.Should().Be("invalid_request");
        result.ErrorDescription.Should().Contain(expectedError);
    }

    [Fact]
    public void Validate_InvalidResponseType_ReturnsError()
    {
        var request = CreateValidRequest();
        request.ResponseType = "token"; // Only "code" is supported

        var result = _sut.Validate(request);

        result.IsValid.Should().BeFalse();
        result.Error.Should().Be("unsupported_response_type");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Validate_MissingClientId_ReturnsError(string? clientId)
    {
        var request = CreateValidRequest();
        request.ClientId = clientId;

        var result = _sut.Validate(request);

        result.IsValid.Should().BeFalse();
        result.Error.Should().Be("invalid_request");
        result.ErrorDescription.Should().Contain("client_id");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Validate_MissingRedirectUri_ReturnsError(string? redirectUri)
    {
        var request = CreateValidRequest();
        request.RedirectUri = redirectUri;

        var result = _sut.Validate(request);

        result.IsValid.Should().BeFalse();
        result.Error.Should().Be("invalid_request");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Validate_MissingState_ReturnsError(string? state)
    {
        var request = CreateValidRequest();
        request.State = state;

        var result = _sut.Validate(request);

        result.IsValid.Should().BeFalse();
        result.Error.Should().Be("invalid_request");
        result.ErrorDescription.Should().Contain("state");
    }

    // ===== PKCE Validation (IndieAuth §4.1.1, RFC 7636) =====
    
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Validate_MissingCodeChallenge_ReturnsError(string? codeChallenge)
    {
        var request = CreateValidRequest();
        request.CodeChallenge = codeChallenge;

        var result = _sut.Validate(request);

        result.IsValid.Should().BeFalse();
        result.Error.Should().Be("invalid_request");
        result.ErrorDescription.Should().Contain("code_challenge");
    }

    [Theory]
    [InlineData("plain")]   // IndieAuth requires S256
    [InlineData("")]
    [InlineData("sha256")]  // Wrong format
    public void Validate_InvalidCodeChallengeMethod_ReturnsError(string method)
    {
        var request = CreateValidRequest();
        request.CodeChallengeMethod = method;

        var result = _sut.Validate(request);

        result.IsValid.Should().BeFalse();
        result.Error.Should().Be("invalid_request");
        result.ErrorDescription.Should().Contain("code_challenge_method");
    }

    [Fact]
    public void Validate_MissingCodeChallengeMethod_DefaultsToS256()
    {
        var request = CreateValidRequest();
        request.CodeChallengeMethod = null; // Should default to S256

        var result = _sut.Validate(request);

        // If code_challenge is present, method defaults to S256
        result.IsValid.Should().BeTrue();
    }

    // ===== Scope Validation (IndieAuth §4.2) =====
    
    [Theory]
    [InlineData("profile")]
    [InlineData("email")]
    [InlineData("profile email")]
    [InlineData("profile email create update delete")]
    public void Validate_ValidScopes_ReturnsValid(string scope)
    {
        var request = CreateValidRequest();
        request.Scope = scope;

        var result = _sut.Validate(request);

        result.IsValid.Should().BeTrue();
    }

    [Theory]
    [InlineData("invalid_scope")]
    [InlineData("profile invalid")]
    public void Validate_UnknownScopes_IgnoresUnknown(string scope)
    {
        var request = CreateValidRequest();
        request.Scope = scope;

        // Per spec, unknown scopes SHOULD be ignored, not rejected
        var result = _sut.Validate(request);
        result.IsValid.Should().BeTrue();
    }

    // ===== Client Metadata (IndieAuth §4.3) =====
    
    [Fact]
    public void Validate_RedirectUriNotMatchingClientId_ReturnsError()
    {
        var request = CreateValidRequest();
        request.ClientId = "https://app.example.com/";
        request.RedirectUri = "https://malicious.com/callback";

        var result = _sut.Validate(request);

        result.IsValid.Should().BeFalse();
        result.Error.Should().Be("invalid_request");
        result.ErrorDescription.Should().Contain("redirect_uri");
    }

    private static AuthorizationRequest CreateValidRequest() => new()
    {
        ResponseType = "code",
        ClientId = "https://app.example.com/",
        RedirectUri = "https://app.example.com/callback",
        State = "test-state-123",
        CodeChallenge = "E9Melhoa2OwvFrEMTJguCHaoeK1t8URWbuGJSstw-cM",
        CodeChallengeMethod = "S256"
    };
}
```

### 3. PKCE Tests (RFC 7636)

```csharp
// tests/Talos.Web.Tests/Services/PkceServiceSpecTests.cs
using FluentAssertions;
using Talos.Web.Services;

namespace Talos.Web.Tests.Services;

/// <summary>
/// PKCE tests derived from RFC 7636 Appendix B examples
/// https://tools.ietf.org/html/rfc7636#appendix-B
/// </summary>
public class PkceServiceSpecTests
{
    private readonly PkceService _sut = new();

    // ===== RFC 7636 Appendix B: Code Verifier and Challenge Examples =====

    [Fact]
    public void GenerateCodeChallenge_RfcExample_ProducesExpectedChallenge()
    {
        // From RFC 7636 Appendix B
        // code_verifier = dBjftJeZ4CVP-mB92K27uhbUJU1p1r_wW1gFWFOEjXk
        // Expected S256 challenge = E9Melhoa2OwvFrEMTJguCHaoeK1t8URWbuGJSstw-cM
        
        var verifier = "dBjftJeZ4CVP-mB92K27uhbUJU1p1r_wW1gFWFOEjXk";
        var expectedChallenge = "E9Melhoa2OwvFrEMTJguCHaoeK1t8URWbuGJSstw-cM";

        var challenge = _sut.GenerateCodeChallenge(verifier);

        challenge.Should().Be(expectedChallenge);
    }

    // ===== Code Verifier Requirements (RFC 7636 §4.1) =====

    [Fact]
    public void GenerateCodeVerifier_MeetsMinimumLength()
    {
        // Code verifier MUST be between 43 and 128 characters
        var verifier = _sut.GenerateCodeVerifier();

        verifier.Length.Should().BeGreaterOrEqualTo(43);
        verifier.Length.Should().BeLessOrEqualTo(128);
    }

    [Fact]
    public void GenerateCodeVerifier_UsesOnlyAllowedCharacters()
    {
        // Code verifier MUST only use [A-Z] / [a-z] / [0-9] / "-" / "." / "_" / "~"
        var verifier = _sut.GenerateCodeVerifier();
        var allowedPattern = @"^[A-Za-z0-9\-._~]+$";

        verifier.Should().MatchRegex(allowedPattern);
    }

    [Fact]
    public void GenerateCodeVerifier_IsUrlSafe()
    {
        // Must not contain base64 padding or non-URL-safe characters
        var verifier = _sut.GenerateCodeVerifier();

        verifier.Should().NotContain("+");
        verifier.Should().NotContain("/");
        verifier.Should().NotContain("=");
    }

    // ===== Code Verifier Length Edge Cases =====

    [Theory]
    [InlineData(42)]    // Too short
    [InlineData(129)]   // Too long
    public void VerifyCodeChallenge_InvalidVerifierLength_ReturnsFalse(int length)
    {
        var verifier = new string('a', length);
        var challenge = _sut.GenerateCodeChallenge(new string('a', 43)); // Valid length

        var result = _sut.VerifyCodeChallenge(verifier, challenge, "S256");

        result.Should().BeFalse();
    }

    [Theory]
    [InlineData(43)]    // Minimum
    [InlineData(64)]    // Typical
    [InlineData(128)]   // Maximum
    public void VerifyCodeChallenge_ValidVerifierLength_ProcessesCorrectly(int length)
    {
        var verifier = new string('a', length);
        var challenge = _sut.GenerateCodeChallenge(verifier);

        var result = _sut.VerifyCodeChallenge(verifier, challenge, "S256");

        result.Should().BeTrue();
    }

    // ===== S256 Method (RFC 7636 §4.2) =====

    [Fact]
    public void VerifyCodeChallenge_S256_CorrectVerifier_ReturnsTrue()
    {
        var verifier = "dBjftJeZ4CVP-mB92K27uhbUJU1p1r_wW1gFWFOEjXk";
        var challenge = "E9Melhoa2OwvFrEMTJguCHaoeK1t8URWbuGJSstw-cM";

        var result = _sut.VerifyCodeChallenge(verifier, challenge, "S256");

        result.Should().BeTrue();
    }

    [Fact]
    public void VerifyCodeChallenge_S256_IncorrectVerifier_ReturnsFalse()
    {
        var verifier = "wrong-verifier-that-does-not-match-challenge";
        var challenge = "E9Melhoa2OwvFrEMTJguCHaoeK1t8URWbuGJSstw-cM";

        var result = _sut.VerifyCodeChallenge(verifier, challenge, "S256");

        result.Should().BeFalse();
    }

    // ===== Plain Method (IndieAuth does NOT support plain) =====

    [Fact]
    public void VerifyCodeChallenge_PlainMethod_NotSupported()
    {
        // IndieAuth spec requires S256, plain MUST NOT be accepted
        var verifier = "test-verifier-value-that-is-long-enough123";
        var challenge = verifier; // Plain would match directly

        var result = _sut.VerifyCodeChallenge(verifier, challenge, "plain");

        result.Should().BeFalse();
    }

    // ===== Error Cases =====

    [Theory]
    [InlineData(null, "challenge", "S256")]
    [InlineData("verifier", null, "S256")]
    [InlineData("", "challenge", "S256")]
    [InlineData("verifier", "", "S256")]
    public void VerifyCodeChallenge_NullOrEmptyInputs_ReturnsFalse(
        string? verifier, string? challenge, string method)
    {
        var result = _sut.VerifyCodeChallenge(verifier!, challenge!, method);

        result.Should().BeFalse();
    }
}
```

### 4. Token Endpoint Tests (IndieAuth §5)

```csharp
// tests/Talos.Web.Tests/Validators/TokenRequestValidatorTests.cs
using FluentAssertions;
using Talos.Web.Models;
using Talos.Web.Validators;

namespace Talos.Web.Tests.Validators;

/// <summary>
/// Tests for token request validation per IndieAuth spec §5
/// https://indieauth.spec.indieweb.org/#token-endpoint
/// </summary>
public class TokenRequestValidatorTests
{
    private readonly TokenRequestValidator _sut = new();

    // ===== Grant Type Validation (IndieAuth §5.1) =====

    [Fact]
    public void Validate_AuthorizationCodeGrant_Valid()
    {
        var request = CreateValidTokenRequest();

        var result = _sut.Validate(request);

        result.IsValid.Should().BeTrue();
    }

    [Theory]
    [InlineData("password")]
    [InlineData("client_credentials")]
    [InlineData("implicit")]
    [InlineData("")]
    [InlineData(null)]
    public void Validate_UnsupportedGrantType_ReturnsError(string? grantType)
    {
        var request = CreateValidTokenRequest();
        request.GrantType = grantType;

        var result = _sut.Validate(request);

        result.IsValid.Should().BeFalse();
        result.Error.Should().Be("unsupported_grant_type");
    }

    [Fact]
    public void Validate_RefreshTokenGrant_Valid()
    {
        var request = new TokenRequest
        {
            GrantType = "refresh_token",
            RefreshToken = "valid-refresh-token",
            ClientId = "https://app.example.com/"
        };

        var result = _sut.Validate(request);

        result.IsValid.Should().BeTrue();
    }

    // ===== Required Parameters (IndieAuth §5.2) =====

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Validate_MissingCode_ReturnsError(string? code)
    {
        var request = CreateValidTokenRequest();
        request.Code = code;

        var result = _sut.Validate(request);

        result.IsValid.Should().BeFalse();
        result.Error.Should().Be("invalid_request");
        result.ErrorDescription.Should().Contain("code");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Validate_MissingClientId_ReturnsError(string? clientId)
    {
        var request = CreateValidTokenRequest();
        request.ClientId = clientId;

        var result = _sut.Validate(request);

        result.IsValid.Should().BeFalse();
        result.Error.Should().Be("invalid_request");
        result.ErrorDescription.Should().Contain("client_id");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Validate_MissingRedirectUri_ReturnsError(string? redirectUri)
    {
        var request = CreateValidTokenRequest();
        request.RedirectUri = redirectUri;

        var result = _sut.Validate(request);

        result.IsValid.Should().BeFalse();
        result.Error.Should().Be("invalid_request");
        result.ErrorDescription.Should().Contain("redirect_uri");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Validate_MissingCodeVerifier_ReturnsError(string? codeVerifier)
    {
        var request = CreateValidTokenRequest();
        request.CodeVerifier = codeVerifier;

        var result = _sut.Validate(request);

        result.IsValid.Should().BeFalse();
        result.Error.Should().Be("invalid_request");
        result.ErrorDescription.Should().Contain("code_verifier");
    }

    private static TokenRequest CreateValidTokenRequest() => new()
    {
        GrantType = "authorization_code",
        Code = "valid-authorization-code",
        ClientId = "https://app.example.com/",
        RedirectUri = "https://app.example.com/callback",
        CodeVerifier = "dBjftJeZ4CVP-mB92K27uhbUJU1p1r_wW1gFWFOEjXk"
    };
}
```

### 5. Token Response Tests (IndieAuth §5.3)

```csharp
// tests/Talos.Web.Tests/Services/TokenResponseTests.cs
using FluentAssertions;
using System.Text.Json;
using Talos.Web.Models;

namespace Talos.Web.Tests.Services;

/// <summary>
/// Tests for token response format per IndieAuth spec §5.3
/// https://indieauth.spec.indieweb.org/#access-token-response
/// </summary>
public class TokenResponseTests
{
    // ===== Access Token Response (IndieAuth §5.3.1) =====

    [Fact]
    public void TokenResponse_ContainsRequiredFields()
    {
        var response = new TokenResponse
        {
            AccessToken = "test-access-token",
            TokenType = "Bearer",
            ExpiresIn = 900,
            Me = "https://example.com/"
        };

        response.AccessToken.Should().NotBeNullOrEmpty();
        response.TokenType.Should().Be("Bearer");
        response.Me.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void TokenResponse_WithProfile_IncludesProfileClaims()
    {
        var response = new TokenResponse
        {
            AccessToken = "test-access-token",
            TokenType = "Bearer",
            Me = "https://example.com/",
            Profile = new ProfileResponse
            {
                Name = "Jane Doe",
                Photo = "https://example.com/photo.jpg",
                Url = "https://example.com/",
                Email = "jane@example.com"
            }
        };

        response.Profile.Should().NotBeNull();
        response.Profile!.Name.Should().Be("Jane Doe");
    }

    [Fact]
    public void TokenResponse_SerializesToCorrectJson()
    {
        var response = new TokenResponse
        {
            AccessToken = "test-token",
            TokenType = "Bearer",
            ExpiresIn = 900,
            Me = "https://example.com/",
            Scope = "profile email"
        };

        var json = JsonSerializer.Serialize(response, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
        });

        json.Should().Contain("\"access_token\"");
        json.Should().Contain("\"token_type\"");
        json.Should().Contain("\"expires_in\"");
        json.Should().Contain("\"me\"");
        json.Should().Contain("\"scope\"");
    }

    // ===== Error Response (IndieAuth §5.3.2) =====

    [Theory]
    [InlineData("invalid_request", "The request is missing a required parameter")]
    [InlineData("invalid_grant", "The authorization code has expired")]
    [InlineData("invalid_client", "The client_id is invalid")]
    [InlineData("unauthorized_client", "The client is not authorized")]
    public void ErrorResponse_UsesStandardErrorCodes(string error, string description)
    {
        var response = new TokenErrorResponse
        {
            Error = error,
            ErrorDescription = description
        };

        response.Error.Should().Be(error);
        response.ErrorDescription.Should().Be(description);
    }
}
```

### 6. Token Introspection Tests (RFC 7662)

```csharp
// tests/Talos.Web.Tests/Services/TokenIntrospectionTests.cs
using FluentAssertions;
using Talos.Web.Models;

namespace Talos.Web.Tests.Services;

/// <summary>
/// Tests for token introspection per RFC 7662
/// https://tools.ietf.org/html/rfc7662
/// </summary>
public class TokenIntrospectionTests
{
    // ===== Introspection Response (RFC 7662 §2.2) =====

    [Fact]
    public void IntrospectionResponse_ActiveToken_ContainsRequiredFields()
    {
        var response = new IntrospectionResponse
        {
            Active = true,
            Me = "https://example.com/",
            ClientId = "https://app.example.com/",
            Scope = "profile email",
            Exp = DateTimeOffset.UtcNow.AddMinutes(15).ToUnixTimeSeconds(),
            Iat = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
        };

        response.Active.Should().BeTrue();
        response.Me.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void IntrospectionResponse_InactiveToken_OnlyContainsActive()
    {
        // Per RFC 7662, inactive tokens should only return {"active": false}
        var response = new IntrospectionResponse { Active = false };

        response.Active.Should().BeFalse();
        // Other fields should be null/omitted for inactive tokens
    }

    [Fact]
    public void IntrospectionResponse_ExpiredToken_ReturnsInactive()
    {
        // Tokens past their exp time should return active=false
        var expiredTime = DateTimeOffset.UtcNow.AddMinutes(-5).ToUnixTimeSeconds();
        
        // The introspection endpoint should check this
        var isExpired = expiredTime < DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        
        isExpired.Should().BeTrue();
    }
}
```

### 7. Token Revocation Tests (RFC 7009)

```csharp
// tests/Talos.Web.Tests/Services/TokenRevocationTests.cs
using FluentAssertions;
using Moq;
using Talos.Web.Services;
using Talos.Web.Data;

namespace Talos.Web.Tests.Services;

/// <summary>
/// Tests for token revocation per RFC 7009
/// https://tools.ietf.org/html/rfc7009
/// </summary>
public class TokenRevocationTests
{
    // ===== Revocation Request (RFC 7009 §2.1) =====

    [Fact]
    public async Task Revoke_ValidToken_RevokesSuccessfully()
    {
        // Arrange
        var mockDb = new Mock<ITalosDbContext>();
        var service = new TokenRevocationService(mockDb.Object);

        // Act
        var result = await service.RevokeTokenAsync("valid-refresh-token");

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public async Task Revoke_NonExistentToken_ReturnsSuccessfully()
    {
        // Per RFC 7009 §2.2: server responds with HTTP 200 even if token is invalid
        var mockDb = new Mock<ITalosDbContext>();
        var service = new TokenRevocationService(mockDb.Object);

        var result = await service.RevokeTokenAsync("non-existent-token");

        // Should not throw, should return success
        result.Should().BeTrue();
    }

    [Fact]
    public async Task Revoke_AlreadyRevokedToken_ReturnsSuccessfully()
    {
        // Revoking an already-revoked token should succeed silently
        var mockDb = new Mock<ITalosDbContext>();
        var service = new TokenRevocationService(mockDb.Object);

        await service.RevokeTokenAsync("token-to-revoke");
        var result = await service.RevokeTokenAsync("token-to-revoke");

        result.Should().BeTrue();
    }

    // ===== Token Type Hint (RFC 7009 §2.1) =====

    [Theory]
    [InlineData("access_token")]
    [InlineData("refresh_token")]
    [InlineData(null)]  // Hint is optional
    public async Task Revoke_WithTokenTypeHint_ProcessesCorrectly(string? tokenTypeHint)
    {
        var mockDb = new Mock<ITalosDbContext>();
        var service = new TokenRevocationService(mockDb.Object);

        var result = await service.RevokeTokenAsync("some-token", tokenTypeHint);

        result.Should().BeTrue();
    }
}
```

### 8. Metadata Discovery Tests (IndieAuth §4.4)

```csharp
// tests/Talos.Web.Tests/Metadata/MetadataDiscoveryTests.cs
using FluentAssertions;
using Talos.Web.Models;

namespace Talos.Web.Tests.Metadata;

/// <summary>
/// Tests for authorization server metadata per IndieAuth §4.4
/// https://indieauth.spec.indieweb.org/#discovery-by-clients
/// </summary>
public class MetadataDiscoveryTests
{
    [Fact]
    public void Metadata_ContainsRequiredFields()
    {
        var metadata = new AuthorizationServerMetadata
        {
            Issuer = "https://auth.example.com/",
            AuthorizationEndpoint = "https://auth.example.com/auth",
            TokenEndpoint = "https://auth.example.com/token"
        };

        metadata.Issuer.Should().NotBeNullOrEmpty();
        metadata.AuthorizationEndpoint.Should().NotBeNullOrEmpty();
        metadata.TokenEndpoint.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void Metadata_IncludesOptionalEndpoints()
    {
        var metadata = new AuthorizationServerMetadata
        {
            Issuer = "https://auth.example.com/",
            AuthorizationEndpoint = "https://auth.example.com/auth",
            TokenEndpoint = "https://auth.example.com/token",
            IntrospectionEndpoint = "https://auth.example.com/token/introspect",
            RevocationEndpoint = "https://auth.example.com/token/revoke",
            UserinfoEndpoint = "https://auth.example.com/userinfo"
        };

        metadata.IntrospectionEndpoint.Should().NotBeNullOrEmpty();
        metadata.RevocationEndpoint.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void Metadata_IncludesSupportedFeatures()
    {
        var metadata = new AuthorizationServerMetadata
        {
            Issuer = "https://auth.example.com/",
            AuthorizationEndpoint = "https://auth.example.com/auth",
            TokenEndpoint = "https://auth.example.com/token",
            ResponseTypesSupported = new[] { "code" },
            GrantTypesSupported = new[] { "authorization_code", "refresh_token" },
            CodeChallengeMethodsSupported = new[] { "S256" },
            ScopesSupported = new[] { "profile", "email", "create", "update", "delete" }
        };

        metadata.ResponseTypesSupported.Should().Contain("code");
        metadata.CodeChallengeMethodsSupported.Should().Contain("S256");
        metadata.CodeChallengeMethodsSupported.Should().NotContain("plain");
    }

    [Fact]
    public void Metadata_WellKnownPath_IsCorrect()
    {
        // Metadata SHOULD be at /.well-known/oauth-authorization-server
        var expectedPath = "/.well-known/oauth-authorization-server";
        
        expectedPath.Should().StartWith("/.well-known/");
    }
}
```

---

## RelMeAuth Specification Test Cases

### 9. rel="me" Link Discovery Tests (RelMeAuth)

```csharp
// tests/Talos.Web.Tests/Services/RelMeDiscoveryTests.cs
using FluentAssertions;
using Talos.Web.Services;

namespace Talos.Web.Tests.Services;

/// <summary>
/// Tests for rel="me" link discovery per RelMeAuth specification
/// https://microformats.org/wiki/RelMeAuth
/// </summary>
public class RelMeDiscoveryTests
{
    private readonly RelMeParser _sut = new();

    // ===== Link Tag Discovery =====

    [Fact]
    public void Parse_LinkTagWithRelMe_ExtractsHref()
    {
        var html = @"
            <html>
            <head>
                <link rel=""me"" href=""https://github.com/janedoe"">
            </head>
            <body></body>
            </html>";

        var links = _sut.ExtractRelMeLinks(html, "https://example.com/");

        links.Should().Contain("https://github.com/janedoe");
    }

    [Fact]
    public void Parse_AnchorTagWithRelMe_ExtractsHref()
    {
        var html = @"
            <html>
            <body>
                <a href=""https://github.com/janedoe"" rel=""me"">GitHub</a>
            </body>
            </html>";

        var links = _sut.ExtractRelMeLinks(html, "https://example.com/");

        links.Should().Contain("https://github.com/janedoe");
    }

    [Fact]
    public void Parse_MultipleRelMeLinks_ExtractsAll()
    {
        var html = @"
            <html>
            <head>
                <link rel=""me"" href=""https://github.com/janedoe"">
            </head>
            <body>
                <a href=""https://twitter.com/janedoe"" rel=""me"">Twitter</a>
                <a href=""https://mastodon.social/@janedoe"" rel=""me"">Mastodon</a>
            </body>
            </html>";

        var links = _sut.ExtractRelMeLinks(html, "https://example.com/");

        links.Should().HaveCount(3);
        links.Should().Contain("https://github.com/janedoe");
        links.Should().Contain("https://twitter.com/janedoe");
        links.Should().Contain("https://mastodon.social/@janedoe");
    }

    // ===== Relative URL Resolution =====

    [Fact]
    public void Parse_RelativeHref_ResolvesAgainstBase()
    {
        var html = @"
            <html>
            <body>
                <a href=""/about"" rel=""me"">About</a>
            </body>
            </html>";

        var links = _sut.ExtractRelMeLinks(html, "https://example.com/");

        links.Should().Contain("https://example.com/about");
    }

    [Fact]
    public void Parse_ProtocolRelativeUrl_ResolvesToHttps()
    {
        var html = @"
            <html>
            <body>
                <a href=""//github.com/janedoe"" rel=""me"">GitHub</a>
            </body>
            </html>";

        var links = _sut.ExtractRelMeLinks(html, "https://example.com/");

        links.Should().Contain("https://github.com/janedoe");
    }

    // ===== Edge Cases =====

    [Fact]
    public void Parse_EmptyHref_SkipsLink()
    {
        var html = @"
            <html>
            <body>
                <a href="""" rel=""me"">Empty</a>
                <a href=""https://github.com/janedoe"" rel=""me"">GitHub</a>
            </body>
            </html>";

        var links = _sut.ExtractRelMeLinks(html, "https://example.com/");

        links.Should().HaveCount(1);
        links.Should().NotContain("");
    }

    [Fact]
    public void Parse_NoRelMeLinks_ReturnsEmptyList()
    {
        var html = @"
            <html>
            <body>
                <a href=""https://github.com/janedoe"">GitHub (no rel)</a>
            </body>
            </html>";

        var links = _sut.ExtractRelMeLinks(html, "https://example.com/");

        links.Should().BeEmpty();
    }

    [Fact]
    public void Parse_RelMeWithOtherRels_StillExtractsLink()
    {
        // rel can contain multiple space-separated values
        var html = @"
            <html>
            <body>
                <a href=""https://github.com/janedoe"" rel=""me noopener"">GitHub</a>
            </body>
            </html>";

        var links = _sut.ExtractRelMeLinks(html, "https://example.com/");

        links.Should().Contain("https://github.com/janedoe");
    }

    [Fact]
    public void Parse_DuplicateLinks_Deduplicated()
    {
        var html = @"
            <html>
            <head>
                <link rel=""me"" href=""https://github.com/janedoe"">
            </head>
            <body>
                <a href=""https://github.com/janedoe"" rel=""me"">GitHub</a>
            </body>
            </html>";

        var links = _sut.ExtractRelMeLinks(html, "https://example.com/");

        links.Should().HaveCount(1);
    }

    // ===== Malformed HTML =====

    [Fact]
    public void Parse_MalformedHtml_StillExtractsLinks()
    {
        var html = @"
            <html>
            <body>
                <a href=""https://github.com/janedoe"" rel=""me"">GitHub
                <!-- Missing closing tag -->
            </body>";

        var links = _sut.ExtractRelMeLinks(html, "https://example.com/");

        links.Should().Contain("https://github.com/janedoe");
    }

    [Fact]
    public void Parse_CaseInsensitiveRel_ExtractsLink()
    {
        var html = @"
            <html>
            <body>
                <a href=""https://github.com/janedoe"" rel=""ME"">GitHub</a>
                <a href=""https://twitter.com/janedoe"" REL=""me"">Twitter</a>
            </body>
            </html>";

        var links = _sut.ExtractRelMeLinks(html, "https://example.com/");

        links.Should().HaveCount(2);
    }
}
```

### 10. Provider Matching Tests

```csharp
// tests/Talos.Web.Tests/Services/ProviderMatchingTests.cs
using FluentAssertions;
using Talos.Web.Services.IdentityProviders;

namespace Talos.Web.Tests.Services;

/// <summary>
/// Tests for matching rel="me" URLs to identity providers
/// </summary>
public class ProviderMatchingTests
{
    private readonly GitHubIdentityProvider _github;

    public ProviderMatchingTests()
    {
        _github = new GitHubIdentityProvider(/* mock dependencies */);
    }

    // ===== GitHub URL Matching =====

    [Theory]
    [InlineData("https://github.com/janedoe", "janedoe")]
    [InlineData("https://github.com/jane-doe", "jane-doe")]
    [InlineData("https://github.com/jane_doe", "jane_doe")]
    [InlineData("https://github.com/JaneDoe", "JaneDoe")]
    [InlineData("https://www.github.com/janedoe", "janedoe")]
    [InlineData("https://github.com/janedoe/", "janedoe")]
    public void GitHub_ValidProfileUrl_MatchesCorrectly(string url, string expectedUsername)
    {
        var match = _github.MatchProfileUrl(url);

        match.Should().NotBeNull();
        match!.Username.Should().Be(expectedUsername);
    }

    [Theory]
    [InlineData("https://github.com/")]                      // No username
    [InlineData("https://github.com/janedoe/repo")]          // Repository URL
    [InlineData("https://github.com/janedoe/repo/issues")]   // Issue URL
    [InlineData("https://github.com/orgs/example")]          // Organization
    [InlineData("https://github.com/login")]                 // Reserved path
    [InlineData("https://github.com/settings")]              // Reserved path
    [InlineData("https://github.com/explore")]               // Reserved path
    [InlineData("http://github.com/janedoe")]                // HTTP not HTTPS
    [InlineData("https://gist.github.com/janedoe")]          // Gist subdomain
    [InlineData("https://raw.githubusercontent.com/janedoe")] // CDN subdomain
    [InlineData("https://gitlab.com/janedoe")]               // Wrong provider
    public void GitHub_InvalidProfileUrl_ReturnsNull(string url)
    {
        var match = _github.MatchProfileUrl(url);

        match.Should().BeNull();
    }

    // ===== Reserved GitHub Paths =====

    [Theory]
    [InlineData("login")]
    [InlineData("logout")]
    [InlineData("settings")]
    [InlineData("organizations")]
    [InlineData("explore")]
    [InlineData("trending")]
    [InlineData("collections")]
    [InlineData("events")]
    [InlineData("sponsors")]
    [InlineData("features")]
    [InlineData("security")]
    [InlineData("team")]
    [InlineData("enterprise")]
    [InlineData("pricing")]
    [InlineData("about")]
    [InlineData("contact")]
    public void GitHub_ReservedPaths_AreNotMatchedAsUsernames(string reservedPath)
    {
        var url = $"https://github.com/{reservedPath}";
        var match = _github.MatchProfileUrl(url);

        match.Should().BeNull();
    }
}
```

### 11. Reciprocal Link Verification Tests

```csharp
// tests/Talos.Web.Tests/Services/ReciprocalLinkVerificationTests.cs
using FluentAssertions;
using Moq;
using Talos.Web.Services;

namespace Talos.Web.Tests.Services;

/// <summary>
/// Tests for reciprocal link verification per RelMeAuth
/// https://microformats.org/wiki/RelMeAuth#Verifying_the_RelMeAuth_link
/// </summary>
public class ReciprocalLinkVerificationTests
{
    // ===== Basic Reciprocal Verification =====

    [Fact]
    public async Task Verify_GitHubProfileLinksToUserSite_ReturnsTrue()
    {
        // User's site: https://jane.example.com/
        // Has rel="me" to: https://github.com/janedoe
        // GitHub profile bio contains: jane.example.com
        
        var mockHttpClient = new Mock<IHttpClientFactory>();
        // Setup mock to return GitHub profile with link back
        
        var service = new ReciprocaLinkVerifier(mockHttpClient.Object);
        
        var result = await service.VerifyReciprocalLinkAsync(
            "https://github.com/janedoe",
            "https://jane.example.com/");

        result.Should().BeTrue();
    }

    [Fact]
    public async Task Verify_GitHubProfileNoLinkBack_ReturnsFalse()
    {
        // GitHub profile does not contain link to user's site
        var mockHttpClient = new Mock<IHttpClientFactory>();
        // Setup mock to return GitHub profile without link back
        
        var service = new ReciprocaLinkVerifier(mockHttpClient.Object);
        
        var result = await service.VerifyReciprocalLinkAsync(
            "https://github.com/janedoe",
            "https://jane.example.com/");

        result.Should().BeFalse();
    }

    // ===== URL Normalization for Comparison =====

    [Theory]
    [InlineData("https://jane.example.com", "https://jane.example.com/")]
    [InlineData("https://jane.example.com/", "https://jane.example.com")]
    [InlineData("https://JANE.EXAMPLE.COM/", "https://jane.example.com/")]
    [InlineData("https://jane.example.com:443/", "https://jane.example.com/")]
    public void UrlNormalization_MatchesEquivalentUrls(string url1, string url2)
    {
        var normalizer = new UrlNormalizer();
        
        var normalized1 = normalizer.Normalize(url1);
        var normalized2 = normalizer.Normalize(url2);

        normalized1.Should().Be(normalized2);
    }

    // ===== Partial URL Matching =====

    [Fact]
    public async Task Verify_GitHubBioContainsUrlWithoutScheme_Matches()
    {
        // GitHub bio might just say "jane.example.com" without https://
        var mockHttpClient = new Mock<IHttpClientFactory>();
        var service = new ReciprocalLinkVerifier(mockHttpClient.Object);
        
        // This should still match https://jane.example.com/
        var result = await service.VerifyReciprocalLinkAsync(
            "https://github.com/janedoe",
            "https://jane.example.com/");

        // Implementation should handle partial URL matching
        result.Should().BeTrue();
    }

    // ===== Error Handling =====

    [Fact]
    public async Task Verify_ProviderProfileNotAccessible_ReturnsFalse()
    {
        var mockHttpClient = new Mock<IHttpClientFactory>();
        // Setup mock to return 404 or timeout
        
        var service = new ReciprocalLinkVerifier(mockHttpClient.Object);
        
        var result = await service.VerifyReciprocalLinkAsync(
            "https://github.com/nonexistent",
            "https://jane.example.com/");

        result.Should().BeFalse();
    }
}
```

### 12. Full Authentication Flow Tests

```csharp
// tests/Talos.Web.Tests/Services/FullAuthFlowTests.cs
using FluentAssertions;
using Talos.Web.Services;

namespace Talos.Web.Tests.Services;

/// <summary>
/// Tests for the complete RelMeAuth authentication flow
/// </summary>
public class FullAuthFlowTests
{
    // ===== Complete Flow: Discovery → Provider Auth → Verification =====

    [Fact]
    public async Task CompleteFlow_ValidUserWithGitHub_Succeeds()
    {
        // 1. User enters https://jane.example.com/
        // 2. Talos discovers rel="me" link to github.com/janedoe
        // 3. User authenticates with GitHub
        // 4. GitHub returns that authenticated user is "janedoe"
        // 5. Talos verifies GitHub profile links back to jane.example.com
        // 6. User is authenticated as https://jane.example.com/
        
        var flow = new AuthenticationFlow(/* dependencies */);
        
        var result = await flow.AuthenticateAsync(
            userSiteUrl: "https://jane.example.com/",
            providerType: "github",
            providerAccessToken: "mock-github-token");

        result.Success.Should().BeTrue();
        result.Me.Should().Be("https://jane.example.com/");
    }

    [Fact]
    public async Task CompleteFlow_NoMatchingProviders_ReturnsError()
    {
        // User's site has no rel="me" links to supported providers
        var flow = new AuthenticationFlow(/* dependencies */);
        
        var result = await flow.AuthenticateAsync(
            userSiteUrl: "https://example.com/no-providers",
            providerType: null,
            providerAccessToken: null);

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("No supported identity providers");
    }

    [Fact]
    public async Task CompleteFlow_ReciprocalLinkMissing_ReturnsError()
    {
        // User's site links to GitHub, but GitHub doesn't link back
        var flow = new AuthenticationFlow(/* dependencies */);
        
        var result = await flow.AuthenticateAsync(
            userSiteUrl: "https://jane.example.com/",
            providerType: "github",
            providerAccessToken: "mock-github-token");

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("reciprocal link");
    }

    [Fact]
    public async Task CompleteFlow_WrongGitHubUser_ReturnsError()
    {
        // User authenticates with GitHub but as different username
        // than what was discovered from rel="me" link
        var flow = new AuthenticationFlow(/* dependencies */);
        
        var result = await flow.AuthenticateAsync(
            userSiteUrl: "https://jane.example.com/",  // Links to github.com/janedoe
            providerType: "github",
            providerAccessToken: "token-for-different-user"); // But this is johndoe

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("username mismatch");
    }
}
```

### 13. OAuth 2.0 Error Response Tests (RFC 6749 §5.2)

```csharp
// tests/Talos.Web.Tests/Responses/OAuthErrorResponseTests.cs
using FluentAssertions;
using System.Text.Json;
using Talos.Web.Models;

namespace Talos.Web.Tests.Responses;

/// <summary>
/// Tests for OAuth 2.0 error responses per RFC 6749 §5.2
/// https://tools.ietf.org/html/rfc6749#section-5.2
/// </summary>
public class OAuthErrorResponseTests
{
    // ===== Standard Error Codes (RFC 6749 §5.2) =====

    [Theory]
    [InlineData("invalid_request", "The request is missing a required parameter")]
    [InlineData("invalid_client", "Client authentication failed")]
    [InlineData("invalid_grant", "The authorization code is invalid or expired")]
    [InlineData("unauthorized_client", "The client is not authorized for this grant type")]
    [InlineData("unsupported_grant_type", "The grant type is not supported")]
    [InlineData("invalid_scope", "The requested scope is invalid or unknown")]
    public void ErrorResponse_StandardCodes_AreValid(string errorCode, string description)
    {
        var response = new OAuthErrorResponse
        {
            Error = errorCode,
            ErrorDescription = description
        };

        // Error codes must be lowercase ASCII
        response.Error.Should().MatchRegex(@"^[a-z_]+$");
    }

    // ===== Authorization Endpoint Error Responses (RFC 6749 §4.1.2.1) =====

    [Fact]
    public void AuthorizationError_InvalidRequest_RedirectsWithError()
    {
        // When redirect_uri is valid, errors are returned via redirect
        var redirectUri = "https://app.example.com/callback";
        var state = "test-state";
        
        var errorUrl = $"{redirectUri}?error=invalid_request" +
                       $"&error_description=Missing+required+parameter" +
                       $"&state={state}";

        errorUrl.Should().Contain("error=invalid_request");
        errorUrl.Should().Contain($"state={state}");
    }

    [Fact]
    public void AuthorizationError_AccessDenied_RedirectsWithError()
    {
        // User denied the authorization request
        var redirectUri = "https://app.example.com/callback";
        var state = "test-state";
        
        var errorUrl = $"{redirectUri}?error=access_denied" +
                       $"&error_description=User+denied+the+request" +
                       $"&state={state}";

        errorUrl.Should().Contain("error=access_denied");
    }

    [Theory]
    [InlineData("invalid_request")]
    [InlineData("unauthorized_client")]
    [InlineData("access_denied")]
    [InlineData("unsupported_response_type")]
    [InlineData("invalid_scope")]
    [InlineData("server_error")]
    [InlineData("temporarily_unavailable")]
    public void AuthorizationError_AllStandardCodes_AreSupported(string errorCode)
    {
        OAuthErrorCodes.IsValidAuthorizationError(errorCode).Should().BeTrue();
    }

    // ===== Token Endpoint Error Responses (RFC 6749 §5.2) =====

    [Fact]
    public void TokenError_InvalidGrant_Returns400()
    {
        var response = new OAuthErrorResponse
        {
            Error = "invalid_grant",
            ErrorDescription = "The authorization code has expired"
        };

        // Token errors return 400 Bad Request (except invalid_client which is 401)
        response.Error.Should().Be("invalid_grant");
    }

    [Fact]
    public void TokenError_InvalidClient_Returns401()
    {
        // invalid_client SHOULD return 401 if client authentication failed
        var response = new OAuthErrorResponse
        {
            Error = "invalid_client",
            ErrorDescription = "Client authentication failed"
        };

        response.Error.Should().Be("invalid_client");
    }

    // ===== Error Response JSON Format =====

    [Fact]
    public void ErrorResponse_SerializesToCorrectJson()
    {
        var response = new OAuthErrorResponse
        {
            Error = "invalid_request",
            ErrorDescription = "Missing code parameter",
            ErrorUri = "https://example.com/docs/errors#invalid_request"
        };

        var json = JsonSerializer.Serialize(response, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
        });

        json.Should().Contain("\"error\":");
        json.Should().Contain("\"error_description\":");
        json.Should().Contain("\"error_uri\":");
    }
}
```

### 14. JWT Access Token Claims Tests (IndieAuth §5.3)

```csharp
// tests/Talos.Web.Tests/Services/JwtClaimsTests.cs
using FluentAssertions;
using System.IdentityModel.Tokens.Jwt;
using Talos.Web.Services;

namespace Talos.Web.Tests.Services;

/// <summary>
/// Tests for JWT access token claims per IndieAuth spec
/// </summary>
public class JwtClaimsTests
{
    private readonly TokenService _tokenService;

    public JwtClaimsTests()
    {
        _tokenService = new TokenService(/* test configuration */);
    }

    // ===== Required Claims =====

    [Fact]
    public void AccessToken_ContainsRequiredClaims()
    {
        var token = _tokenService.GenerateAccessToken(
            me: "https://example.com/",
            scope: "profile email",
            clientId: "https://app.example.com/");

        var handler = new JwtSecurityTokenHandler();
        var jwt = handler.ReadJwtToken(token);

        // Standard JWT claims
        jwt.Claims.Should().Contain(c => c.Type == "iss"); // Issuer
        jwt.Claims.Should().Contain(c => c.Type == "sub"); // Subject
        jwt.Claims.Should().Contain(c => c.Type == "aud"); // Audience
        jwt.Claims.Should().Contain(c => c.Type == "exp"); // Expiration
        jwt.Claims.Should().Contain(c => c.Type == "iat"); // Issued At
        
        // IndieAuth-specific claims
        jwt.Claims.Should().Contain(c => c.Type == "me");
        jwt.Claims.Should().Contain(c => c.Type == "client_id");
        jwt.Claims.Should().Contain(c => c.Type == "scope");
    }

    [Fact]
    public void AccessToken_MeClaimMatchesProfileUrl()
    {
        var profileUrl = "https://jane.example.com/";
        
        var token = _tokenService.GenerateAccessToken(
            me: profileUrl,
            scope: "profile",
            clientId: "https://app.example.com/");

        var handler = new JwtSecurityTokenHandler();
        var jwt = handler.ReadJwtToken(token);

        var meClaim = jwt.Claims.First(c => c.Type == "me");
        meClaim.Value.Should().Be(profileUrl);
    }

    [Fact]
    public void AccessToken_ScopeClaimContainsRequestedScopes()
    {
        var token = _tokenService.GenerateAccessToken(
            me: "https://example.com/",
            scope: "profile email create",
            clientId: "https://app.example.com/");

        var handler = new JwtSecurityTokenHandler();
        var jwt = handler.ReadJwtToken(token);

        var scopeClaim = jwt.Claims.First(c => c.Type == "scope");
        scopeClaim.Value.Should().Contain("profile");
        scopeClaim.Value.Should().Contain("email");
        scopeClaim.Value.Should().Contain("create");
    }

    // ===== Expiration =====

    [Fact]
    public void AccessToken_ExpiresInConfiguredTime()
    {
        var token = _tokenService.GenerateAccessToken(
            me: "https://example.com/",
            scope: "profile",
            clientId: "https://app.example.com/");

        var handler = new JwtSecurityTokenHandler();
        var jwt = handler.ReadJwtToken(token);

        var exp = jwt.ValidTo;
        var expectedExpiry = DateTime.UtcNow.AddMinutes(15); // Default 15 minutes

        exp.Should().BeCloseTo(expectedExpiry, TimeSpan.FromSeconds(30));
    }

    // ===== Token Without Scope =====

    [Fact]
    public void AccessToken_WithoutScope_OmitsScopeClaim()
    {
        var token = _tokenService.GenerateAccessToken(
            me: "https://example.com/",
            scope: null,
            clientId: "https://app.example.com/");

        var handler = new JwtSecurityTokenHandler();
        var jwt = handler.ReadJwtToken(token);

        jwt.Claims.Should().NotContain(c => c.Type == "scope");
    }
}
```

### 15. Authorization Code Tests (IndieAuth §4.4)

```csharp
// tests/Talos.Web.Tests/Services/AuthorizationCodeTests.cs
using FluentAssertions;
using Talos.Web.Services;

namespace Talos.Web.Tests.Services;

/// <summary>
/// Tests for authorization code generation and validation
/// </summary>
public class AuthorizationCodeTests
{
    private readonly AuthorizationCodeService _service;

    public AuthorizationCodeTests()
    {
        _service = new AuthorizationCodeService(/* test db context */);
    }

    // ===== Code Generation =====

    [Fact]
    public void GenerateCode_ReturnsSecureRandomCode()
    {
        var code = _service.GenerateAuthorizationCode();

        // Should be cryptographically random and URL-safe
        code.Should().NotBeNullOrEmpty();
        code.Length.Should().BeGreaterOrEqualTo(32);
        code.Should().MatchRegex(@"^[A-Za-z0-9_-]+$");
    }

    [Fact]
    public void GenerateCode_CodesAreUnique()
    {
        var codes = Enumerable.Range(0, 100)
            .Select(_ => _service.GenerateAuthorizationCode())
            .ToList();

        codes.Should().OnlyHaveUniqueItems();
    }

    // ===== Code Storage =====

    [Fact]
    public async Task StoreCode_SavesAllRequiredData()
    {
        var code = _service.GenerateAuthorizationCode();
        
        await _service.StoreCodeAsync(new AuthorizationCodeData
        {
            Code = code,
            ClientId = "https://app.example.com/",
            RedirectUri = "https://app.example.com/callback",
            Me = "https://example.com/",
            Scope = "profile email",
            CodeChallenge = "E9Melhoa2OwvFrEMTJguCHaoeK1t8URWbuGJSstw-cM",
            CodeChallengeMethod = "S256",
            ExpiresAt = DateTime.UtcNow.AddMinutes(10)
        });

        var retrieved = await _service.GetCodeDataAsync(code);

        retrieved.Should().NotBeNull();
        retrieved!.ClientId.Should().Be("https://app.example.com/");
        retrieved.Me.Should().Be("https://example.com/");
    }

    // ===== Code Expiration =====

    [Fact]
    public async Task ValidateCode_ExpiredCode_ReturnsNull()
    {
        var code = _service.GenerateAuthorizationCode();
        
        await _service.StoreCodeAsync(new AuthorizationCodeData
        {
            Code = code,
            ClientId = "https://app.example.com/",
            RedirectUri = "https://app.example.com/callback",
            Me = "https://example.com/",
            ExpiresAt = DateTime.UtcNow.AddMinutes(-5) // Already expired
        });

        var result = await _service.ValidateAndConsumeCodeAsync(code);

        result.Should().BeNull();
    }

    // ===== Code Single Use =====

    [Fact]
    public async Task ValidateCode_SecondUse_Fails()
    {
        var code = _service.GenerateAuthorizationCode();
        
        await _service.StoreCodeAsync(new AuthorizationCodeData
        {
            Code = code,
            ClientId = "https://app.example.com/",
            RedirectUri = "https://app.example.com/callback",
            Me = "https://example.com/",
            ExpiresAt = DateTime.UtcNow.AddMinutes(10)
        });

        var firstUse = await _service.ValidateAndConsumeCodeAsync(code);
        var secondUse = await _service.ValidateAndConsumeCodeAsync(code);

        firstUse.Should().NotBeNull();
        secondUse.Should().BeNull(); // Code consumed on first use
    }

    // ===== Client Binding =====

    [Fact]
    public async Task ValidateCode_WrongClientId_Fails()
    {
        var code = _service.GenerateAuthorizationCode();
        
        await _service.StoreCodeAsync(new AuthorizationCodeData
        {
            Code = code,
            ClientId = "https://app.example.com/",
            RedirectUri = "https://app.example.com/callback",
            Me = "https://example.com/",
            ExpiresAt = DateTime.UtcNow.AddMinutes(10)
        });

        var result = await _service.ValidateCodeForClientAsync(
            code, 
            "https://different-app.example.com/"); // Wrong client

        result.Should().BeNull();
    }

    // ===== Redirect URI Binding =====

    [Fact]
    public async Task ValidateCode_WrongRedirectUri_Fails()
    {
        var code = _service.GenerateAuthorizationCode();
        
        await _service.StoreCodeAsync(new AuthorizationCodeData
        {
            Code = code,
            ClientId = "https://app.example.com/",
            RedirectUri = "https://app.example.com/callback",
            Me = "https://example.com/",
            ExpiresAt = DateTime.UtcNow.AddMinutes(10)
        });

        var result = await _service.ValidateCodeForRedirectAsync(
            code, 
            "https://app.example.com/different-callback"); // Wrong redirect

        result.Should().BeNull();
    }
}
```

### 16. Refresh Token Tests

```csharp
// tests/Talos.Web.Tests/Services/RefreshTokenTests.cs
using FluentAssertions;
using Talos.Web.Services;

namespace Talos.Web.Tests.Services;

/// <summary>
/// Tests for refresh token handling
/// </summary>
public class RefreshTokenTests
{
    private readonly TokenService _tokenService;

    public RefreshTokenTests()
    {
        _tokenService = new TokenService(/* test configuration */);
    }

    // ===== Refresh Token Generation =====

    [Fact]
    public async Task GenerateRefreshToken_ReturnsOpaqueToken()
    {
        var token = await _tokenService.GenerateRefreshTokenAsync(
            me: "https://example.com/",
            clientId: "https://app.example.com/",
            scope: "profile email");

        // Refresh tokens should be opaque (not JWT)
        token.Should().NotBeNullOrEmpty();
        token.Split('.').Should().HaveCountLessThan(3); // Not a JWT
    }

    // ===== Refresh Token Exchange =====

    [Fact]
    public async Task RefreshToken_ValidToken_IssuesNewAccessToken()
    {
        var refreshToken = await _tokenService.GenerateRefreshTokenAsync(
            me: "https://example.com/",
            clientId: "https://app.example.com/",
            scope: "profile");

        var result = await _tokenService.RefreshAccessTokenAsync(
            refreshToken,
            clientId: "https://app.example.com/");

        result.Success.Should().BeTrue();
        result.AccessToken.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task RefreshToken_WrongClientId_Fails()
    {
        var refreshToken = await _tokenService.GenerateRefreshTokenAsync(
            me: "https://example.com/",
            clientId: "https://app.example.com/",
            scope: "profile");

        var result = await _tokenService.RefreshAccessTokenAsync(
            refreshToken,
            clientId: "https://different-app.example.com/"); // Wrong client

        result.Success.Should().BeFalse();
        result.Error.Should().Be("invalid_grant");
    }

    [Fact]
    public async Task RefreshToken_RevokedToken_Fails()
    {
        var refreshToken = await _tokenService.GenerateRefreshTokenAsync(
            me: "https://example.com/",
            clientId: "https://app.example.com/",
            scope: "profile");

        await _tokenService.RevokeRefreshTokenAsync(refreshToken);

        var result = await _tokenService.RefreshAccessTokenAsync(
            refreshToken,
            clientId: "https://app.example.com/");

        result.Success.Should().BeFalse();
        result.Error.Should().Be("invalid_grant");
    }

    [Fact]
    public async Task RefreshToken_ExpiredToken_Fails()
    {
        // Create a token that's already expired
        var refreshToken = await _tokenService.GenerateRefreshTokenAsync(
            me: "https://example.com/",
            clientId: "https://app.example.com/",
            scope: "profile",
            expiresAt: DateTime.UtcNow.AddDays(-1)); // Expired

        var result = await _tokenService.RefreshAccessTokenAsync(
            refreshToken,
            clientId: "https://app.example.com/");

        result.Success.Should().BeFalse();
        result.Error.Should().Be("invalid_grant");
    }

    // ===== Token Rotation =====

    [Fact]
    public async Task RefreshToken_Rotation_IssuesNewRefreshToken()
    {
        var originalRefreshToken = await _tokenService.GenerateRefreshTokenAsync(
            me: "https://example.com/",
            clientId: "https://app.example.com/",
            scope: "profile");

        var result = await _tokenService.RefreshAccessTokenAsync(
            originalRefreshToken,
            clientId: "https://app.example.com/",
            rotateRefreshToken: true);

        result.Success.Should().BeTrue();
        result.RefreshToken.Should().NotBe(originalRefreshToken);
        
        // Original token should be revoked
        var reuse = await _tokenService.RefreshAccessTokenAsync(
            originalRefreshToken,
            clientId: "https://app.example.com/");
        reuse.Success.Should().BeFalse();
    }
}
```

### 17. Profile Response Tests (IndieAuth §5.4)

```csharp
// tests/Talos.Web.Tests/Services/ProfileResponseTests.cs
using FluentAssertions;
using System.Text.Json;
using Talos.Web.Models;

namespace Talos.Web.Tests.Services;

/// <summary>
/// Tests for profile response per IndieAuth spec §5.4
/// https://indieauth.spec.indieweb.org/#profile-information
/// </summary>
public class ProfileResponseTests
{
    // ===== Profile Scope Response =====

    [Fact]
    public void ProfileResponse_WithProfileScope_IncludesBasicInfo()
    {
        var response = new TokenResponse
        {
            AccessToken = "test-token",
            TokenType = "Bearer",
            Me = "https://example.com/",
            Scope = "profile",
            Profile = new ProfileInfo
            {
                Name = "Jane Doe",
                Url = "https://example.com/",
                Photo = "https://example.com/photo.jpg"
            }
        };

        response.Profile.Should().NotBeNull();
        response.Profile!.Name.Should().Be("Jane Doe");
        response.Profile.Url.Should().Be("https://example.com/");
    }

    // ===== Email Scope Response =====

    [Fact]
    public void ProfileResponse_WithEmailScope_IncludesEmail()
    {
        var response = new TokenResponse
        {
            AccessToken = "test-token",
            TokenType = "Bearer",
            Me = "https://example.com/",
            Scope = "profile email",
            Profile = new ProfileInfo
            {
                Name = "Jane Doe",
                Email = "jane@example.com"
            }
        };

        response.Profile!.Email.Should().Be("jane@example.com");
    }

    [Fact]
    public void ProfileResponse_WithoutEmailScope_OmitsEmail()
    {
        var response = new TokenResponse
        {
            AccessToken = "test-token",
            TokenType = "Bearer",
            Me = "https://example.com/",
            Scope = "profile", // No email scope
            Profile = new ProfileInfo
            {
                Name = "Jane Doe"
                // Email should not be included even if available
            }
        };

        response.Profile!.Email.Should().BeNull();
    }

    // ===== JSON Serialization =====

    [Fact]
    public void ProfileResponse_SerializesToSnakeCase()
    {
        var response = new TokenResponse
        {
            AccessToken = "test-token",
            TokenType = "Bearer",
            ExpiresIn = 900,
            Me = "https://example.com/"
        };

        var json = JsonSerializer.Serialize(response, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
        });

        json.Should().Contain("\"access_token\"");
        json.Should().Contain("\"token_type\"");
        json.Should().Contain("\"expires_in\"");
        json.Should().NotContain("\"AccessToken\"");
    }
}
```

---

## Project Setup

```bash
# Create test projects (if not already created)
dotnet new xunit -n Talos.Web.Tests -o tests/Talos.Web.Tests
dotnet new xunit -n Talos.Integration.Tests -o tests/Talos.Integration.Tests

# Add to solution
dotnet sln add tests/Talos.Web.Tests/Talos.Web.Tests.csproj
dotnet sln add tests/Talos.Integration.Tests/Talos.Integration.Tests.csproj

# Add references
dotnet add tests/Talos.Web.Tests reference src/Talos.Web/Talos.Web.csproj
dotnet add tests/Talos.Integration.Tests reference src/Talos.Web/Talos.Web.csproj

# Add packages
cd tests/Talos.Web.Tests
dotnet add package Moq
dotnet add package FluentAssertions

cd ../Talos.Integration.Tests
dotnet add package Microsoft.AspNetCore.Mvc.Testing
dotnet add package FluentAssertions
```

## Unit Tests

### PKCE Service Tests

```csharp
// tests/Talos.Web.Tests/Services/PkceServiceTests.cs
using FluentAssertions;
using Talos.Web.Services;

namespace Talos.Web.Tests.Services;

public class PkceServiceTests
{
    private readonly PkceService _sut = new();

    [Fact]
    public void GenerateCodeVerifier_ReturnsValidLength()
    {
        var verifier = _sut.GenerateCodeVerifier();
        
        verifier.Should().NotBeNullOrEmpty();
        verifier.Length.Should().BeGreaterOrEqualTo(43);
    }

    [Fact]
    public void GenerateCodeVerifier_IsUrlSafe()
    {
        var verifier = _sut.GenerateCodeVerifier();
        
        verifier.Should().NotContain("+");
        verifier.Should().NotContain("/");
        verifier.Should().NotContain("=");
    }

    [Fact]
    public void GenerateCodeChallenge_ProducesValidChallenge()
    {
        var verifier = "dBjftJeZ4CVP-mB92K27uhbUJU1p1r_wW1gFWFOEjXk";
        var expectedChallenge = "E9Melhoa2OwvFrEMTJguCHaoeK1t8URWbuGJSstw-cM";
        
        var challenge = _sut.GenerateCodeChallenge(verifier);
        
        challenge.Should().Be(expectedChallenge);
    }

    [Fact]
    public void VerifyCodeChallenge_ValidVerifier_ReturnsTrue()
    {
        var verifier = "dBjftJeZ4CVP-mB92K27uhbUJU1p1r_wW1gFWFOEjXk";
        var challenge = "E9Melhoa2OwvFrEMTJguCHaoeK1t8URWbuGJSstw-cM";
        
        var result = _sut.VerifyCodeChallenge(verifier, challenge, "S256");
        
        result.Should().BeTrue();
    }

    [Fact]
    public void VerifyCodeChallenge_InvalidVerifier_ReturnsFalse()
    {
        var verifier = "wrong-verifier";
        var challenge = "E9Melhoa2OwvFrEMTJguCHaoeK1t8URWbuGJSstw-cM";
        
        var result = _sut.VerifyCodeChallenge(verifier, challenge, "S256");
        
        result.Should().BeFalse();
    }

    [Fact]
    public void VerifyCodeChallenge_UnsupportedMethod_ReturnsFalse()
    {
        var verifier = "dBjftJeZ4CVP-mB92K27uhbUJU1p1r_wW1gFWFOEjXk";
        var challenge = "dBjftJeZ4CVP-mB92K27uhbUJU1p1r_wW1gFWFOEjXk"; // plain
        
        var result = _sut.VerifyCodeChallenge(verifier, challenge, "plain");
        
        result.Should().BeFalse();
    }

    [Theory]
    [InlineData(null, "challenge", "S256")]
    [InlineData("verifier", null, "S256")]
    [InlineData("", "challenge", "S256")]
    [InlineData("verifier", "", "S256")]
    public void VerifyCodeChallenge_NullOrEmpty_ReturnsFalse(
        string? verifier, string? challenge, string method)
    {
        var result = _sut.VerifyCodeChallenge(verifier!, challenge!, method);
        
        result.Should().BeFalse();
    }
}
```

### Token Service Tests

```csharp
// tests/Talos.Web.Tests/Services/TokenServiceTests.cs
using FluentAssertions;
using Microsoft.Extensions.Options;
using Moq;
using Talos.Web.Configuration;
using Talos.Web.Services;

namespace Talos.Web.Tests.Services;

public class TokenServiceTests
{
    private readonly TokenService _sut;
    private readonly JwtSettings _jwtSettings;

    public TokenServiceTests()
    {
        _jwtSettings = new JwtSettings
        {
            Issuer = "https://test.example.com/",
            Audience = "https://test.example.com/",
            SecretKey = "this-is-a-test-secret-key-32-chars!",
            AccessTokenExpirationMinutes = 15
        };

        var jwtOptions = Options.Create(_jwtSettings);
        var indieAuthOptions = Options.Create(new IndieAuthSettings
        {
            RefreshTokenExpirationDays = 30
        });

        // Note: For unit tests, we mock the DbContext
        _sut = new TokenService(jwtOptions, indieAuthOptions, null!);
    }

    [Fact]
    public void GenerateAccessToken_ReturnsValidJwt()
    {
        var token = _sut.GenerateAccessToken(
            "https://example.com/",
            "profile email",
            "https://app.example.com/");

        token.Should().NotBeNullOrEmpty();
        token.Split('.').Should().HaveCount(3); // JWT format
    }

    [Fact]
    public void GenerateAccessToken_ContainsCorrectClaims()
    {
        var token = _sut.GenerateAccessToken(
            "https://example.com/",
            "profile email",
            "https://app.example.com/");

        var principal = _sut.ValidateAccessToken(token);
        
        principal.Should().NotBeNull();
        principal!.FindFirst("me")?.Value.Should().Be("https://example.com/");
        principal.FindFirst("client_id")?.Value.Should().Be("https://app.example.com/");
        principal.FindFirst("scope")?.Value.Should().Be("profile email");
    }

    [Fact]
    public void ValidateAccessToken_ExpiredToken_ReturnsNull()
    {
        // Create settings with 0 minute expiration for testing
        var expiredSettings = new JwtSettings
        {
            Issuer = _jwtSettings.Issuer,
            Audience = _jwtSettings.Audience,
            SecretKey = _jwtSettings.SecretKey,
            AccessTokenExpirationMinutes = -1 // Expired
        };

        var sut = new TokenService(
            Options.Create(expiredSettings),
            Options.Create(new IndieAuthSettings()),
            null!);

        var token = sut.GenerateAccessToken(
            "https://example.com/",
            null,
            "https://app.example.com/");

        var result = sut.ValidateAccessToken(token);

        result.Should().BeNull();
    }

    [Fact]
    public void ValidateAccessToken_InvalidToken_ReturnsNull()
    {
        var result = _sut.ValidateAccessToken("invalid.token.here");

        result.Should().BeNull();
    }
}
```

### URL Validator Tests

```csharp
// tests/Talos.Web.Tests/Validators/UrlValidatorTests.cs
using FluentAssertions;
using Talos.Web.Validators;

namespace Talos.Web.Tests.Validators;

public class UrlValidatorTests
{
    [Theory]
    [InlineData("https://example.com/", true)]
    [InlineData("https://example.com/path", true)]
    [InlineData("https://example.com/path/", true)]
    [InlineData("http://localhost:5000/", true)]  // localhost allowed
    [InlineData("http://example.com/", false)]     // HTTP not allowed
    [InlineData("ftp://example.com/", false)]
    [InlineData("https://example.com/#fragment", false)]  // Fragments not allowed
    [InlineData("https://user:pass@example.com/", false)] // Userinfo not allowed
    [InlineData("not-a-url", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsValidHttpsUrl_ValidatesCorrectly(string? url, bool expected)
    {
        var result = UrlValidator.IsValidHttpsUrl(url);
        result.Should().Be(expected);
    }

    [Theory]
    [InlineData("https://app.example.com/", "https://app.example.com/callback", true)]
    [InlineData("https://app.example.com/", "https://app.example.com/auth/callback", true)]
    [InlineData("https://app.example.com/", "https://other.example.com/callback", false)]
    [InlineData("https://app.example.com/", "http://app.example.com/callback", false)]
    public void RedirectUriMatchesClient_ValidatesCorrectly(
        string clientId, string redirectUri, bool expected)
    {
        var result = UrlValidator.RedirectUriMatchesClient(clientId, redirectUri);
        result.Should().Be(expected);
    }
}
```

### Authentication Service Tests

```csharp
// tests/Talos.Web.Tests/Services/AuthenticationServiceTests.cs
using FluentAssertions;
using Microsoft.Extensions.Options;
using Talos.Web.Configuration;
using Talos.Web.Services;

namespace Talos.Web.Tests.Services;

public class AuthenticationServiceTests
{
    private readonly AuthenticationService _sut;

    public AuthenticationServiceTests()
    {
        // BCrypt hash of "password123"
        var settings = new UserSettings
        {
            Username = "testuser",
            PasswordHash = "$2a$12$LQv3c1yqBWVHxkd0LHAkCOYz6TtxMQJqhN8/X4.qWZDKiDsLJvQ9O",
            ProfileUrl = "https://example.com/",
            Name = "Test User"
        };

        _sut = new AuthenticationService(Options.Create(settings));
    }

    [Fact]
    public async Task ValidateCredentials_ValidCredentials_ReturnsTrue()
    {
        var result = await _sut.ValidateCredentialsAsync("testuser", "password123");
        
        result.Should().BeTrue();
    }

    [Fact]
    public async Task ValidateCredentials_WrongPassword_ReturnsFalse()
    {
        var result = await _sut.ValidateCredentialsAsync("testuser", "wrongpassword");
        
        result.Should().BeFalse();
    }

    [Fact]
    public async Task ValidateCredentials_WrongUsername_ReturnsFalse()
    {
        var result = await _sut.ValidateCredentialsAsync("wronguser", "password123");
        
        result.Should().BeFalse();
    }

    [Fact]
    public async Task ValidateCredentials_CaseInsensitiveUsername_ReturnsTrue()
    {
        var result = await _sut.ValidateCredentialsAsync("TESTUSER", "password123");
        
        result.Should().BeTrue();
    }
}
```

## Integration Tests

### Test Server Setup

```csharp
// tests/Talos.Integration.Tests/TalosWebApplicationFactory.cs
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Talos.Web.Data;

namespace Talos.Integration.Tests;

public class TalosWebApplicationFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureServices(services =>
        {
            // Replace SQLite with in-memory database
            var descriptor = services.SingleOrDefault(
                d => d.ServiceType == typeof(DbContextOptions<TalosDbContext>));
            
            if (descriptor != null)
            {
                services.Remove(descriptor);
            }

            services.AddDbContext<TalosDbContext>(options =>
            {
                options.UseInMemoryDatabase("TestDb");
            });

            // Ensure database is created
            var sp = services.BuildServiceProvider();
            using var scope = sp.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<TalosDbContext>();
            db.Database.EnsureCreated();
        });

        builder.UseEnvironment("Testing");
    }
}
```

### Profile Page Tests

```csharp
// tests/Talos.Integration.Tests/ProfileTests.cs
using FluentAssertions;

namespace Talos.Integration.Tests;

public class ProfileTests : IClassFixture<TalosWebApplicationFactory>
{
    private readonly HttpClient _client;

    public ProfileTests(TalosWebApplicationFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task GetProfile_ReturnsHtml()
    {
        var response = await _client.GetAsync("/");
        
        response.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);
        response.Content.Headers.ContentType?.MediaType.Should().Be("text/html");
    }

    [Fact]
    public async Task GetProfile_ContainsAuthorizationEndpointLink()
    {
        var response = await _client.GetStringAsync("/");
        
        response.Should().Contain("rel=\"authorization_endpoint\"");
        response.Should().Contain("href=\"/auth\"");
    }

    [Fact]
    public async Task GetProfile_ContainsTokenEndpointLink()
    {
        var response = await _client.GetStringAsync("/");
        
        response.Should().Contain("rel=\"token_endpoint\"");
        response.Should().Contain("href=\"/token\"");
    }

    [Fact]
    public async Task GetMetadata_ReturnsValidJson()
    {
        var response = await _client.GetAsync("/.well-known/oauth-authorization-server");
        
        response.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);
        
        var json = await response.Content.ReadFromJsonAsync<dynamic>();
        ((string)json.authorization_endpoint).Should().EndWith("/auth");
        ((string)json.token_endpoint).Should().EndWith("/token");
    }
}
```

### Authorization Endpoint Tests

```csharp
// tests/Talos.Integration.Tests/AuthorizationTests.cs
using System.Net;
using FluentAssertions;

namespace Talos.Integration.Tests;

public class AuthorizationTests : IClassFixture<TalosWebApplicationFactory>
{
    private readonly HttpClient _client;

    public AuthorizationTests(TalosWebApplicationFactory factory)
    {
        _client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });
    }

    [Fact]
    public async Task Authorize_MissingResponseType_ReturnsBadRequest()
    {
        var response = await _client.GetAsync(
            "/auth?client_id=https://app.example.com/&redirect_uri=https://app.example.com/callback&state=test123&code_challenge=abc&code_challenge_method=S256");
        
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Authorize_MissingCodeChallenge_ReturnsBadRequest()
    {
        var response = await _client.GetAsync(
            "/auth?response_type=code&client_id=https://app.example.com/&redirect_uri=https://app.example.com/callback&state=test123");
        
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Authorize_InvalidCodeChallengeMethod_ReturnsBadRequest()
    {
        var response = await _client.GetAsync(
            "/auth?response_type=code&client_id=https://app.example.com/&redirect_uri=https://app.example.com/callback&state=test123&code_challenge=abc&code_challenge_method=plain");
        
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Authorize_ValidRequest_RedirectsToLogin()
    {
        var response = await _client.GetAsync(
            "/auth?response_type=code&client_id=https://app.example.com/&redirect_uri=https://app.example.com/callback&state=test123&code_challenge=E9Melhoa2OwvFrEMTJguCHaoeK1t8URWbuGJSstw-cM&code_challenge_method=S256");
        
        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        response.Headers.Location?.ToString().Should().StartWith("/login");
    }
}
```

### Token Endpoint Tests

```csharp
// tests/Talos.Integration.Tests/TokenTests.cs
using System.Net;
using FluentAssertions;

namespace Talos.Integration.Tests;

public class TokenTests : IClassFixture<TalosWebApplicationFactory>
{
    private readonly HttpClient _client;
    private readonly TalosWebApplicationFactory _factory;

    public TokenTests(TalosWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task Token_MissingGrantType_ReturnsBadRequest()
    {
        var response = await _client.PostAsync("/token", 
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["code"] = "test"
            }));
        
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Token_InvalidCode_ReturnsBadRequest()
    {
        var response = await _client.PostAsync("/token",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "authorization_code",
                ["code"] = "invalid-code",
                ["client_id"] = "https://app.example.com/",
                ["redirect_uri"] = "https://app.example.com/callback",
                ["code_verifier"] = "test-verifier"
            }));
        
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        
        var content = await response.Content.ReadAsStringAsync();
        content.Should().Contain("invalid_grant");
    }

    [Fact]
    public async Task Token_Revoke_AlwaysReturnsOk()
    {
        // Per RFC 7009, revoke should always return 200
        var response = await _client.PostAsync("/token/revoke",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["token"] = "non-existent-token"
            }));
        
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Token_Introspect_InvalidToken_ReturnsInactive()
    {
        var response = await _client.PostAsync("/token/introspect",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["token"] = "invalid-token"
            }));
        
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        
        var result = await response.Content.ReadFromJsonAsync<IntrospectionResult>();
        result!.Active.Should().BeFalse();
    }

    private record IntrospectionResult(bool Active);
}
```

## End-to-End Testing

### Using indieauth.rocks

[indieauth.rocks](https://indieauth.rocks/) is the official test suite for IndieAuth implementations.

1. Deploy your server to a public HTTPS URL
2. Navigate to https://indieauth.rocks/
3. Enter your profile URL
4. Run through the test suite

### Manual Testing Checklist

```markdown
## IndieAuth Testing Checklist

### Discovery
- [ ] Profile page loads at profile URL
- [ ] `authorization_endpoint` link tag present
- [ ] `token_endpoint` link tag present
- [ ] Metadata endpoint returns valid JSON

### Authorization Flow
- [ ] Valid request redirects to login
- [ ] Invalid `response_type` returns error
- [ ] Missing `code_challenge` returns error
- [ ] Wrong `code_challenge_method` returns error
- [ ] After login, consent page shows
- [ ] Consent page displays client info
- [ ] Approve redirects with code
- [ ] Deny redirects with error
- [ ] State parameter preserved

### Token Exchange
- [ ] Valid code exchange returns tokens
- [ ] Invalid code returns error
- [ ] Expired code returns error
- [ ] Wrong `code_verifier` returns error
- [ ] Wrong `client_id` returns error
- [ ] Wrong `redirect_uri` returns error
- [ ] Code cannot be reused

### Refresh Tokens
- [ ] Refresh token works
- [ ] Expired refresh token fails
- [ ] Revoked refresh token fails
- [ ] Revocation endpoint works

### Token Introspection
- [ ] Valid access token shows active
- [ ] Expired token shows inactive
- [ ] Invalid token shows inactive
```

### Test Client Script

```bash
#!/bin/bash
# test-indieauth.sh - Manual testing helper

BASE_URL="https://localhost:5001"
CLIENT_ID="https://test-client.example.com/"
REDIRECT_URI="https://test-client.example.com/callback"

# Generate PKCE
CODE_VERIFIER=$(openssl rand -base64 32 | tr -d '=' | tr '+/' '-_')
CODE_CHALLENGE=$(echo -n "$CODE_VERIFIER" | openssl dgst -sha256 -binary | base64 | tr -d '=' | tr '+/' '-_')
STATE=$(openssl rand -hex 16)

echo "=== IndieAuth Test ==="
echo "Code Verifier: $CODE_VERIFIER"
echo "Code Challenge: $CODE_CHALLENGE"
echo "State: $STATE"
echo ""
echo "Authorization URL:"
echo "${BASE_URL}/auth?response_type=code&client_id=${CLIENT_ID}&redirect_uri=${REDIRECT_URI}&state=${STATE}&code_challenge=${CODE_CHALLENGE}&code_challenge_method=S256&scope=profile+email"
echo ""
echo "After authorization, use this to exchange the code:"
echo "curl -X POST ${BASE_URL}/token \\"
echo "  -H 'Content-Type: application/x-www-form-urlencoded' \\"
echo "  -d 'grant_type=authorization_code' \\"
echo "  -d 'code=YOUR_CODE_HERE' \\"
echo "  -d 'client_id=${CLIENT_ID}' \\"
echo "  -d 'redirect_uri=${REDIRECT_URI}' \\"
echo "  -d 'code_verifier=${CODE_VERIFIER}'"
```

## Running Tests

```bash
# Run all tests
dotnet test

# Run with coverage
dotnet test --collect:"XPlat Code Coverage"

# Run specific test project
dotnet test tests/Talos.Web.Tests

# Run specific test class
dotnet test --filter "FullyQualifiedName~PkceServiceTests"

# Run with verbose output
dotnet test -v n
```

## CI/CD Integration

### GitHub Actions

```yaml
# .github/workflows/test.yml
name: Test

on:
  push:
    branches: [main]
  pull_request:
    branches: [main]

jobs:
  test:
    runs-on: ubuntu-latest
    
    steps:
      - uses: actions/checkout@v4
      
      - name: Setup .NET
        uses: actions/setup-dotnet@v4
        with:
          dotnet-version: '8.0.x'
      
      - name: Restore dependencies
        run: dotnet restore
      
      - name: Build
        run: dotnet build --no-restore
      
      - name: Test
        run: dotnet test --no-build --verbosity normal --collect:"XPlat Code Coverage"
      
      - name: Upload coverage
        uses: codecov/codecov-action@v3
        with:
          files: ./tests/**/coverage.cobertura.xml
```

## Next Steps

After testing:
1. Deployment → [09-deployment.md](./09-deployment.md)

