# 08 - Testing

## Overview

This document covers the testing strategy for Talos, including unit tests, integration tests, and end-to-end testing with IndieAuth-compatible tools.

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

