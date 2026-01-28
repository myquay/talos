# Talos - IndieAuth Server Main Plan

## Overview

Talos is an IndieAuth authorization server that allows users to sign in to IndieAuth-compatible applications using their personal website URL. Instead of managing user credentials directly, Talos discovers supported identity providers from the user's website and delegates authentication to third-party providers (starting with GitHub).

**Key Concept**: Talos does not host user profiles. Users point their personal website to Talos as their authorization/token endpoint, and Talos authenticates them via identity providers discovered from their website.

## How It Works

```
┌─────────────┐     ┌─────────────┐     ┌─────────────┐     ┌─────────────┐
│  Client App │     │   User's    │     │   Talos     │     │   GitHub    │
│  (Site A)   │     │  Website    │     │   Server    │     │   OAuth     │
└──────┬──────┘     └──────┬──────┘     └──────┬──────┘     └──────┬──────┘
       │                   │                   │                   │
       │ 1. User enters their website URL      │                   │
       │◄──────────────────│                   │                   │
       │                   │                   │                   │
       │ 2. Discover auth endpoint from user's site                │
       │──────────────────►│                   │                   │
       │                   │                   │                   │
       │ 3. Redirect to Talos /auth            │                   │
       │──────────────────────────────────────►│                   │
       │                   │                   │                   │
       │                   │ 4. Fetch user's site, discover rel="me" links
       │                   │◄──────────────────│                   │
       │                   │                   │                   │
       │                   │ 5. Find supported identity providers  │
       │                   │  (e.g., github.com/username)          │
       │                   │──────────────────►│                   │
       │                   │                   │                   │
       │                   │      6. If multiple, show provider picker
       │                   │         If one, redirect directly     │
       │                   │                   │──────────────────►│
       │                   │                   │                   │
       │                   │      7. User authenticates with GitHub│
       │                   │                   │◄──────────────────│
       │                   │                   │                   │
       │                   │ 8. Verify GitHub profile matches rel="me"
       │                   │                   │                   │
       │                   │ 9. Show consent screen                │
       │                   │                   │                   │
       │ 10. Redirect with authorization code  │                   │
       │◄──────────────────────────────────────│                   │
       │                   │                   │                   │
       │ 11. Exchange code for tokens          │                   │
       │──────────────────────────────────────►│                   │
       │                   │                   │                   │
       │ 12. Return JWT access + refresh token │                   │
       │◄──────────────────────────────────────│                   │
```

## Architecture

```
┌─────────────────────────────────────────────────────────────────┐
│                         Client Browser                          │
└─────────────────────────────────────────────────────────────────┘
                                  │
                                  ▼
┌─────────────────────────────────────────────────────────────────┐
│                      .NET 8 Web Application                      │
│  ┌───────────────────────────────────────────────────────────┐  │
│  │                    Kestrel / Reverse Proxy                 │  │
│  └───────────────────────────────────────────────────────────┘  │
│                                  │                               │
│  ┌─────────────────┐  ┌─────────────────┐  ┌─────────────────┐  │
│  │   Vue.js SPA    │  │  API Controllers │  │   OAuth         │  │
│  │  (Embedded)     │  │  /auth, /token   │  │   Callbacks     │  │
│  └─────────────────┘  └─────────────────┘  └─────────────────┘  │
│           │                    │                    │            │
│           └────────────────────┼────────────────────┘            │
│                                ▼                                 │
│  ┌───────────────────────────────────────────────────────────┐  │
│  │                     Service Layer                          │  │
│  │  ┌─────────────┐  ┌─────────────┐  ┌─────────────────────┐ │  │
│  │  │  Identity   │  │Token Service│  │ Profile Discovery   │ │  │
│  │  │  Providers  │  │             │  │    Service          │ │  │
│  │  │  (GitHub)   │  │             │  │                     │ │  │
│  │  └─────────────┘  └─────────────┘  └─────────────────────┘ │  │
│  └───────────────────────────────────────────────────────────┘  │
│                                │                                 │
│  ┌───────────────────────────────────────────────────────────┐  │
│  │                    Data Access Layer                       │  │
│  │                   (Entity Framework Core)                  │  │
│  └───────────────────────────────────────────────────────────┘  │
│                                │                                 │
└────────────────────────────────┼────────────────────────────────┘
                                 ▼
                    ┌─────────────────────┐
                    │       SQLite        │
                    │   (talos.db)        │
                    └─────────────────────┘
```

## Technology Stack

| Component | Technology | Notes |
|-----------|------------|-------|
| Backend | .NET 8 (ASP.NET Core) | Minimal APIs or Controllers |
| Frontend | Vue.js 3 + Tailwind CSS | Embedded SPA, Vite build |
| Database | SQLite | EF Core with migrations |
| Auth Tokens | JWT (short-lived) | 15-minute access tokens |
| Refresh Tokens | Opaque | Stored in DB, revokable |
| Identity Providers | GitHub OAuth | Extensible for more providers |

## Key Design Decisions

1. **No User Database**: Users are authenticated via third-party providers, not stored credentials
2. **RelMeAuth Discovery**: User's website is fetched to discover `rel="me"` links to identity providers
3. **Embedded SPA**: Vue.js app for provider selection and consent screens
4. **JWT Access Tokens**: Short-lived (15 min) JWTs for stateless API access
5. **Opaque Refresh Tokens**: Long-lived tokens stored in SQLite for revocation capability
6. **PKCE Required**: All authorization flows must use PKCE (S256)
7. **GitHub First**: Starting with GitHub as the initial identity provider

## Supported Identity Providers

| Provider | Discovery | Status |
|----------|-----------|--------|
| GitHub | `rel="me"` link to `github.com/username` | ✅ Planned |
| Twitter/X | `rel="me"` link to `twitter.com/username` | 🔮 Future |
| Mastodon | `rel="me"` link to Mastodon instance | 🔮 Future |
| Email | `rel="me"` link to `mailto:` | 🔮 Future |

## RelMeAuth: How Identity Discovery Works

1. User enters their website URL (e.g., `https://jane.example.com/`)
2. Talos fetches the URL and parses HTML for `rel="me"` links
3. Example HTML on user's site:
   ```html
   <a href="https://github.com/janedoe" rel="me">GitHub</a>
   ```
4. Talos matches `github.com/janedoe` against supported providers
5. User authenticates with GitHub
6. Talos verifies GitHub profile has a reciprocal link back to `jane.example.com`
7. If verified, user is authenticated as `https://jane.example.com/`

## Implementation Phases

### Phase 1: Project Foundation
- [01-project-setup.md](./01-project-setup.md)
- Set up .NET 8 solution structure
- Configure Vue.js with Vite and Tailwind CSS
- Configure GitHub OAuth credentials

### Phase 2: Profile Discovery
- [02-profile-discovery.md](./02-profile-discovery.md)
- Fetch and parse user's website
- Extract `rel="me"` links
- Match against supported identity providers

### Phase 3: Identity Provider Integration
- [05-authentication.md](./05-authentication.md)
- GitHub OAuth integration
- Provider selection UI (if multiple providers)
- RelMeAuth verification (reciprocal link check)

### Phase 4: Authorization Endpoint
- [03-authorization-endpoint.md](./03-authorization-endpoint.md)
- Handle authorization requests
- Client verification
- Consent UI
- Authorization code generation

### Phase 5: Token Endpoint
- [04-token-endpoint.md](./04-token-endpoint.md)
- Code exchange for tokens
- JWT access token generation
- Opaque refresh token management

### Phase 6: Security Hardening
- [06-security.md](./06-security.md)
- PKCE implementation
- CSRF protection
- Rate limiting

### Phase 7: Data Layer
- [07-database.md](./07-database.md)
- SQLite schema design
- EF Core configuration

### Phase 8: Testing & Deployment
- [08-testing.md](./08-testing.md)
- [09-deployment.md](./09-deployment.md)
- Integration with indieauth.rocks
- Docker containerization

## Component Dependencies

```
Phase 1 (Project Setup)
    │
    ├──► Phase 2 (Profile Discovery)
    │         │
    │         ▼
    ├──► Phase 3 (Identity Providers - GitHub)
    │         │
    ├──► Phase 7 (Database) ◄─────────┘
    │         │
    └─────────┼──► Phase 4 (Authorization Endpoint)
              │         │
              │         ▼
              └──► Phase 5 (Token Endpoint)
                        │
                        ▼
                  Phase 6 (Security Hardening)
                        │
                        ▼
                  Phase 8 (Testing & Deployment)
```

## File Structure Preview

```
talos/
├── src/
│   ├── Talos.Web/                    # Main .NET web project
│   │   ├── Controllers/
│   │   │   ├── AuthController.cs     # Authorization endpoint
│   │   │   ├── TokenController.cs    # Token endpoint
│   │   │   └── CallbackController.cs # OAuth callbacks
│   │   │
│   │   ├── Services/
│   │   │   ├── ProfileDiscoveryService.cs
│   │   │   ├── IdentityProviders/
│   │   │   │   ├── IIdentityProvider.cs
│   │   │   │   ├── GitHubIdentityProvider.cs
│   │   │   │   └── IdentityProviderFactory.cs
│   │   │   ├── TokenService.cs
│   │   │   └── AuthorizationService.cs
│   │   │
│   │   ├── Models/
│   │   │   ├── AuthorizationRequest.cs
│   │   │   ├── DiscoveredProvider.cs
│   │   │   └── TokenResponse.cs
│   │   │
│   │   ├── Data/
│   │   │   ├── TalosDbContext.cs
│   │   │   └── Entities/
│   │   │
│   │   ├── ClientApp/                # Vue.js SPA
│   │   │   ├── src/
│   │   │   │   ├── views/
│   │   │   │   │   ├── ProviderSelectView.vue
│   │   │   │   │   ├── ConsentView.vue
│   │   │   │   │   └── ErrorView.vue
│   │   │   │   └── components/
│   │   │   └── ...
│   │   │
│   │   ├── appsettings.json
│   │   └── Program.cs
│   │
│   └── Talos.Core/                   # Shared models/interfaces
│
├── tests/
├── .plans/
├── talos.sln
├── Dockerfile
└── README.md
```

## Success Criteria

1. ✅ Fetch user's website and discover `rel="me"` links
2. ✅ Support GitHub as identity provider
3. ✅ Verify reciprocal link from GitHub back to user's site
4. ✅ Authorization flow completes with PKCE
5. ✅ JWT access tokens issued with correct claims
6. ✅ Refresh tokens work and can be revoked
7. ✅ Passes indieauth.rocks validation
8. ✅ Vue.js UI for provider selection and consent

## User's Website Requirements

For a user to authenticate via Talos, their website must:

1. **Point to Talos** via `<link>` tags:
   ```html
   <link rel="authorization_endpoint" href="https://talos.example.com/auth">
   <link rel="token_endpoint" href="https://talos.example.com/token">
   ```

2. **Include `rel="me"` links** to supported identity providers:
   ```html
   <a href="https://github.com/username" rel="me">GitHub</a>
   ```

3. **Have reciprocal link** on identity provider profile back to their website

## References

- [IndieAuth Specification](https://indieauth.spec.indieweb.org/)
- [RelMeAuth](https://microformats.org/wiki/RelMeAuth)
- [OAuth 2.0 (RFC 6749)](https://tools.ietf.org/html/rfc6749)
- [PKCE (RFC 7636)](https://tools.ietf.org/html/rfc7636)
- [JWT (RFC 7519)](https://tools.ietf.org/html/rfc7519)
- [GitHub OAuth Documentation](https://docs.github.com/en/developers/apps/building-oauth-apps)
