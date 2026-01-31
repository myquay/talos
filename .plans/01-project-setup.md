# 01 - Project Setup

## Implementation Status: ✅ COMPLETED (January 31, 2026)

### What Was Implemented
- Created .NET 10 solution with Talos.Web, Talos.Core, and test projects
- Installed all required NuGet packages (EF Core SQLite, JWT, HtmlAgilityPack)
- Set up Vue.js 3 with Vite, TypeScript, Vue Router, and Tailwind CSS
- Created full ClientApp structure with views, components, and API layer
- Implemented configuration classes (GitHubSettings, JwtSettings, IndieAuthSettings, TalosSettings)
- Set up appsettings.json with full configuration structure
- Created Program.cs with DI configuration, HTTP clients, and SPA static file serving
- Implemented core service interfaces and implementations:
  - IProfileDiscoveryService / ProfileDiscoveryService (rel="me" link discovery)
  - IIdentityProvider / GitHubIdentityProvider (GitHub OAuth)
  - IIdentityProviderFactory / IdentityProviderFactory
  - ITokenService / TokenService (JWT generation/validation)
  - IAuthorizationService / AuthorizationService (authorization flow)
  - IPkceService / PkceService (PKCE S256 validation)
- Created EF Core DbContext with entities for PendingAuthentication, AuthorizationCode, RefreshToken
- Created Vue.js views: ProviderSelectView, ConsentView, ErrorView
- Created Vue.js components: ProviderCard, ClientCard, ScopeList
- Build passes successfully

---

## Overview

This plan covers the initial project structure, tooling, and dependencies for the Talos IndieAuth server. Talos authenticates users via third-party identity providers (starting with GitHub) discovered from the user's website.

## Prerequisites

- .NET 8 SDK
- Node.js 20+ (for Vue.js/Vite)
- SQLite (bundled with .NET)
- GitHub OAuth App credentials

## Solution Structure

```
talos/
├── src/
│   ├── Talos.Web/                    # Main ASP.NET Core project
│   │   ├── Controllers/
│   │   │   ├── AuthController.cs     # Authorization endpoint
│   │   │   ├── TokenController.cs    # Token endpoint
│   │   │   └── CallbackController.cs # OAuth provider callbacks
│   │   │
│   │   ├── Services/
│   │   │   ├── IAuthorizationService.cs
│   │   │   ├── AuthorizationService.cs
│   │   │   ├── ITokenService.cs
│   │   │   ├── TokenService.cs
│   │   │   ├── IClientVerificationService.cs
│   │   │   ├── ClientVerificationService.cs
│   │   │   ├── IProfileDiscoveryService.cs
│   │   │   ├── ProfileDiscoveryService.cs
│   │   │   ├── IPkceService.cs
│   │   │   └── IdentityProviders/
│   │   │       ├── IIdentityProvider.cs
│   │   │       ├── IdentityProviderFactory.cs
│   │   │       └── GitHubIdentityProvider.cs
│   │   │
│   │   ├── Models/
│   │   │   ├── AuthorizationRequest.cs
│   │   │   ├── TokenRequest.cs
│   │   │   ├── TokenResponse.cs
│   │   │   ├── ClientInfo.cs
│   │   │   ├── DiscoveredProvider.cs
│   │   │   └── ProviderVerificationResult.cs
│   │   │
│   │   ├── Data/
│   │   │   ├── TalosDbContext.cs
│   │   │   ├── Entities/
│   │   │   │   ├── AuthorizationCode.cs
│   │   │   │   ├── RefreshToken.cs
│   │   │   │   └── PendingAuthentication.cs
│   │   │   └── Migrations/
│   │   │
│   │   ├── Configuration/
│   │   │   ├── GitHubSettings.cs
│   │   │   ├── JwtSettings.cs
│   │   │   └── IndieAuthSettings.cs
│   │   │
│   │   ├── ClientApp/                # Vue.js SPA (embedded)
│   │   │   ├── src/
│   │   │   │   ├── App.vue
│   │   │   │   ├── main.ts
│   │   │   │   ├── views/
│   │   │   │   │   ├── ProviderSelectView.vue
│   │   │   │   │   ├── ConsentView.vue
│   │   │   │   │   └── ErrorView.vue
│   │   │   │   ├── components/
│   │   │   │   │   ├── ProviderCard.vue
│   │   │   │   │   ├── ClientCard.vue
│   │   │   │   │   └── ScopeList.vue
│   │   │   │   └── api/
│   │   │   │       └── auth.ts
│   │   │   ├── index.html
│   │   │   ├── package.json
│   │   │   ├── vite.config.ts
│   │   │   ├── tailwind.config.js
│   │   │   └── tsconfig.json
│   │   │
│   │   ├── wwwroot/                  # Built Vue.js assets go here
│   │   ├── appsettings.json
│   │   ├── appsettings.Development.json
│   │   ├── Program.cs
│   │   └── Talos.Web.csproj
│   │
│   └── Talos.Core/                   # Shared library (optional)
│       ├── Interfaces/
│       ├── Constants/
│       └── Talos.Core.csproj
│
├── tests/
│   ├── Talos.Web.Tests/              # Unit tests
│   │   └── Talos.Web.Tests.csproj
│   └── Talos.Integration.Tests/      # Integration tests
│       └── Talos.Integration.Tests.csproj
│
├── .plans/                           # Planning documents
├── .gitignore
├── talos.sln
└── README.md
```

## Step-by-Step Setup

### Step 1: Create Solution and Projects

```bash
# Create solution
dotnet new sln -n talos

# Create web project
dotnet new web -n Talos.Web -o src/Talos.Web

# Create core library (optional, for shared types)
dotnet new classlib -n Talos.Core -o src/Talos.Core

# Create test projects
dotnet new xunit -n Talos.Web.Tests -o tests/Talos.Web.Tests
dotnet new xunit -n Talos.Integration.Tests -o tests/Talos.Integration.Tests

# Add projects to solution
dotnet sln add src/Talos.Web/Talos.Web.csproj
dotnet sln add src/Talos.Core/Talos.Core.csproj
dotnet sln add tests/Talos.Web.Tests/Talos.Web.Tests.csproj
dotnet sln add tests/Talos.Integration.Tests/Talos.Integration.Tests.csproj

# Add references
dotnet add src/Talos.Web/Talos.Web.csproj reference src/Talos.Core/Talos.Core.csproj
dotnet add tests/Talos.Web.Tests/Talos.Web.Tests.csproj reference src/Talos.Web/Talos.Web.csproj
```

### Step 2: Install NuGet Packages

```bash
cd src/Talos.Web

# Entity Framework Core + SQLite
dotnet add package Microsoft.EntityFrameworkCore.Sqlite
dotnet add package Microsoft.EntityFrameworkCore.Design

# JWT handling
dotnet add package System.IdentityModel.Tokens.Jwt
dotnet add package Microsoft.AspNetCore.Authentication.JwtBearer

# HTTP client for profile discovery and OAuth
dotnet add package Microsoft.Extensions.Http

# HTML parsing for rel="me" discovery
dotnet add package HtmlAgilityPack
```

### Step 3: Create GitHub OAuth App

1. Go to GitHub → Settings → Developer settings → OAuth Apps
2. Click "New OAuth App"
3. Fill in:
   - **Application name**: Talos IndieAuth
   - **Homepage URL**: `https://talos.example.com`
   - **Authorization callback URL**: `https://talos.example.com/callback/github`
4. Save the **Client ID** and generate a **Client Secret**

### Step 4: Initialize Vue.js with Vite

```bash
cd src/Talos.Web

# Create Vue.js app with Vite
npm create vite@latest ClientApp -- --template vue-ts

cd ClientApp

# Install dependencies
npm install

# Install Tailwind CSS
npm install -D tailwindcss postcss autoprefixer
npx tailwindcss init -p

# Install additional packages
npm install vue-router@4
npm install axios
```

### Step 5: Configure Tailwind CSS

**tailwind.config.js**:
```javascript
/** @type {import('tailwindcss').Config} */
export default {
  content: [
    "./index.html",
    "./src/**/*.{vue,js,ts,jsx,tsx}",
  ],
  theme: {
    extend: {},
  },
  plugins: [],
}
```

**src/style.css**:
```css
@tailwind base;
@tailwind components;
@tailwind utilities;
```

### Step 6: Configure Vite for .NET Integration

**vite.config.ts**:
```typescript
import { defineConfig } from 'vite'
import vue from '@vitejs/plugin-vue'

export default defineConfig({
  plugins: [vue()],
  build: {
    outDir: '../wwwroot',
    emptyOutDir: true,
  },
  server: {
    proxy: {
      '/api': {
        target: 'https://localhost:5001',
        changeOrigin: true,
        secure: false,
      },
      '/auth': {
        target: 'https://localhost:5001',
        changeOrigin: true,
        secure: false,
      },
      '/callback': {
        target: 'https://localhost:5001',
        changeOrigin: true,
        secure: false,
      },
    },
  },
})
```

### Step 7: Configure .NET to Serve SPA

**Program.cs** (key sections):
```csharp
var builder = WebApplication.CreateBuilder(args);

// Add services
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();

// Configure HTTP client for profile discovery and GitHub API
builder.Services.AddHttpClient("ProfileDiscovery");
builder.Services.AddHttpClient("GitHub", client =>
{
    client.BaseAddress = new Uri("https://api.github.com/");
    client.DefaultRequestHeaders.Add("Accept", "application/vnd.github.v3+json");
    client.DefaultRequestHeaders.Add("User-Agent", "Talos-IndieAuth");
});

// Configure SPA static files
builder.Services.AddSpaStaticFiles(configuration =>
{
    configuration.RootPath = "wwwroot";
});

var app = builder.Build();

// Serve static files
app.UseStaticFiles();
app.UseSpaStaticFiles();

app.UseRouting();

app.MapControllers();

// SPA fallback (for Vue Router history mode)
app.MapFallbackToFile("index.html");

app.Run();
```

## Configuration Files

### appsettings.json

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning"
    }
  },
  "AllowedHosts": "*",
  
  "GitHub": {
    "ClientId": "your-github-client-id",
    "ClientSecret": "your-github-client-secret",
    "AuthorizationEndpoint": "https://github.com/login/oauth/authorize",
    "TokenEndpoint": "https://github.com/login/oauth/access_token",
    "UserApiEndpoint": "https://api.github.com/user"
  },
  
  "Jwt": {
    "Issuer": "https://talos.example.com/",
    "Audience": "https://talos.example.com/",
    "SecretKey": "your-256-bit-secret-key-here-min-32-chars",
    "AccessTokenExpirationMinutes": 15
  },
  
  "IndieAuth": {
    "AuthorizationCodeExpirationMinutes": 10,
    "RefreshTokenExpirationDays": 30,
    "PendingAuthenticationExpirationMinutes": 30
  },
  
  "Talos": {
    "BaseUrl": "https://talos.example.com"
  },
  
  "ConnectionStrings": {
    "DefaultConnection": "Data Source=talos.db"
  }
}
```

### Configuration Classes

```csharp
// Configuration/GitHubSettings.cs
public class GitHubSettings
{
    public string ClientId { get; set; } = "";
    public string ClientSecret { get; set; } = "";
    public string AuthorizationEndpoint { get; set; } = "https://github.com/login/oauth/authorize";
    public string TokenEndpoint { get; set; } = "https://github.com/login/oauth/access_token";
    public string UserApiEndpoint { get; set; } = "https://api.github.com/user";
}

// Configuration/JwtSettings.cs
public class JwtSettings
{
    public string Issuer { get; set; } = "";
    public string Audience { get; set; } = "";
    public string SecretKey { get; set; } = "";
    public int AccessTokenExpirationMinutes { get; set; } = 15;
}

// Configuration/IndieAuthSettings.cs
public class IndieAuthSettings
{
    public int AuthorizationCodeExpirationMinutes { get; set; } = 10;
    public int RefreshTokenExpirationDays { get; set; } = 30;
    public int PendingAuthenticationExpirationMinutes { get; set; } = 30;
}
```

### .gitignore additions

```gitignore
# SQLite
*.db
*.db-shm
*.db-wal

# Vue.js
src/Talos.Web/ClientApp/node_modules/
src/Talos.Web/ClientApp/dist/

# Built SPA assets (regenerated on build)
src/Talos.Web/wwwroot/

# Secrets (use environment variables in production)
appsettings.*.json
!appsettings.json
!appsettings.Development.json
```

## Development Workflow

### Running in Development

**Terminal 1 - .NET Backend**:
```bash
cd src/Talos.Web
dotnet watch run
```

**Terminal 2 - Vue.js Dev Server** (with hot reload):
```bash
cd src/Talos.Web/ClientApp
npm run dev
```

The Vite dev server proxies API requests to the .NET backend.

### Building for Production

```bash
# Build Vue.js to wwwroot
cd src/Talos.Web/ClientApp
npm run build

# Build .NET
cd ..
dotnet publish -c Release -o ./publish
```

## MSBuild Integration (Optional)

Add to `Talos.Web.csproj` to auto-build Vue.js:

```xml
<Target Name="BuildClientApp" BeforeTargets="Build" Condition="'$(Configuration)' == 'Release'">
  <Exec WorkingDirectory="ClientApp" Command="npm install" />
  <Exec WorkingDirectory="ClientApp" Command="npm run build" />
</Target>
```

## Next Steps

After project setup:
1. Implement profile discovery → [02-profile-discovery.md](./02-profile-discovery.md)
2. Set up GitHub OAuth → [05-authentication.md](./05-authentication.md)
3. Create the database schema → [07-database.md](./07-database.md)
