# Talos

A minimal IndieAuth authorization server built with .NET 10 and Vue.js. Talos authenticates users via third-party identity providers (starting with GitHub) discovered from the user's website using RelMeAuth.

## Features

- 🔐 **IndieAuth Compliant** - Full implementation of the IndieAuth specification
- 🐙 **GitHub Authentication** - Authenticate users via GitHub OAuth
- 🔍 **RelMeAuth Discovery** - Automatically discover identity providers from `rel="me"` links
- 🔑 **PKCE Support** - Proof Key for Code Exchange (S256) for secure authorization
- 🎫 **JWT Access Tokens** - Short-lived JWT tokens with proper IndieAuth claims
- 🔄 **Refresh Tokens** - Opaque, revokable refresh tokens
- 🚀 **Vue.js Frontend** - Modern SPA for provider selection and consent

## How It Works

1. User enters their website URL (e.g., `https://jane.example.com/`)
2. Talos fetches the website and discovers `rel="me"` links
3. User authenticates with a discovered provider (e.g., GitHub)
4. Talos verifies the GitHub profile links back to the user's website
5. User authorizes the requesting application
6. Application receives tokens to act on behalf of the user

## Quick Start

### Prerequisites

- .NET 10 SDK
- Node.js 20+
- GitHub OAuth App credentials ([create one here](https://github.com/settings/developers))

### Development

```bash
# Clone the repository
git clone https://github.com/yourusername/talos.git
cd talos

# Trust the .NET dev certificate (one-time setup)
dotnet dev-certs https --trust

# Install frontend dependencies
cd src/Talos.Web/ClientApp
npm install
cd ../../..

# Copy and configure settings
cp src/Talos.Web/appsettings.example.json src/Talos.Web/appsettings.Development.json
# Edit appsettings.Development.json with your GitHub OAuth credentials
```

**Running manually:**
```bash
# Terminal 1: Start the API
dotnet run --project src/Talos.Web

# Terminal 2: Start the frontend dev server
cd src/Talos.Web/ClientApp
npm run dev
```

Vite proxies API requests to the .NET backend.


## Configuration

### User's Website Requirements

For a user to authenticate via Talos, their website must:

1. **Point to Talos** via `<link>` tags:
   ```html
   <link rel="authorization_endpoint" href="https://your-talos-domain.com/auth">
   <link rel="token_endpoint" href="https://your-talos-domain.com/token">
   ```

2. **Include `rel="me"` links** to supported identity providers:
   ```html
   <a href="https://github.com/username" rel="me">GitHub</a>
   ```

3. **Have reciprocal link** on the identity provider profile back to their website:
   - **GitHub**: Your website URL must be in your GitHub profile's **Website** field or mentioned in your **Bio**. This is verified during authentication to prove you own both the website and the GitHub account.

### Environment Variables

| Variable | Description |
|----------|-------------|
| `GITHUB_CLIENT_ID` | GitHub OAuth App Client ID |
| `GITHUB_CLIENT_SECRET` | GitHub OAuth App Client Secret |
| `TALOS_BASE_URL` | Base URL where Talos is hosted |
| `JWT_SECRET_KEY` | Secret key for JWT signing (min 32 chars) |

### Personal Server Mode

To restrict Talos to only authenticate users from specific websites (useful for personal servers):

```json
"Talos": {
  "BaseUrl": "https://auth.example.com",
  "AllowedProfileHosts": ["jane.example.com", "blog.jane.example.com"]
}
```

When configured, only users whose `me` URL matches one of the allowed hosts can authenticate. Leave as `null` or omit to allow all hosts (default behavior).

## API Endpoints

| Endpoint | Description |
|----------|-------------|
| `GET /auth` | Authorization endpoint (IndieAuth) |
| `POST /auth` | Authorization code verification (profile-only, no access token) |
| `POST /token` | Token endpoint (code exchange for access token) |
| `POST /token/introspect` | Token introspection (RFC 7662) |
| `POST /token/revoke` | Token revocation (RFC 7009) |
| `GET /.well-known/oauth-authorization-server` | Server metadata |

## Architecture

```
┌───────────────────────────────────────────────────────────-──────┐
│                      .NET 10 Web Application                     │
│  ┌─────────────────┐  ┌───────────-──────┐  ┌─────────────────┐  │
│  │   Vue.js SPA    │  │  API Controllers │  │   OAuth         │  │
│  │  (Embedded)     │  │  /auth, /token   │  │   Callbacks     │  │
│  └─────────────────┘  └────────────-─────┘  └─────────────────┘  │
│                                │                                 │
│  ┌───────────────────────────────────────────-────────────────┐  │
│  │                     Service Layer                          │  │
│  │  ┌─────────────┐  ┌─────────────┐  ┌─────────────────────┐ │  │
│  │  │  Identity   │  │Token Service│  │ Profile Discovery   │ │  │
│  │  │  Providers  │  │             │  │    Service          │ │  │
│  │  └─────────────┘  └─────────────┘  └─────────────────────┘ │  │
│  └─────────────────────────────────────────────-──────────────┘  │
│                                │                                 │
│  ┌──────────────────────────────────────────────-─────────────┐  │
│  │                    SQLite Database                         │  │
│  └─────────────────────────────────────────────────-──────────┘  │
└────────────────────────────────────────────────────-─────────────┘
```

## Testing

```bash
# Run all tests
dotnet test

# Run with coverage
dotnet test --collect:"XPlat Code Coverage"
```

## License

MIT

## References

- [IndieAuth Specification](https://indieauth.spec.indieweb.org/)
- [RelMeAuth](https://microformats.org/wiki/RelMeAuth)
- [OAuth 2.0 (RFC 6749)](https://tools.ietf.org/html/rfc6749)
- [PKCE (RFC 7636)](https://tools.ietf.org/html/rfc7636)
