# 10 - Landing Page & Configuration Status

## Overview

Create a landing page that provides a different experience based on whether Talos is properly configured:

1. **Not Configured**: Show a setup wizard with step-by-step instructions for each missing/invalid configuration
2. **Configured**: Show an informational page about the Talos project and how to use it

## Configuration Validation

The `/api/status` endpoint checks all configuration and returns issues with severity levels.

### Checks to Perform

| Category | Check | Severity | Issue Code |
|----------|-------|----------|------------|
| **GitHub** | ClientId is empty | error | `github_client_id_missing` |
| **GitHub** | ClientSecret is empty | error | `github_client_secret_missing` |
| **JWT** | SecretKey is empty | error | `jwt_secret_key_missing` |
| **JWT** | SecretKey < 32 characters | error | `jwt_secret_key_too_short` |
| **JWT** | Issuer is empty | error | `jwt_issuer_missing` |
| **JWT** | Audience is empty | error | `jwt_audience_missing` |
| **JWT** | AccessTokenExpirationMinutes <= 0 | error | `jwt_expiration_invalid` |
| **IndieAuth** | AuthorizationCodeExpirationMinutes <= 0 | error | `indieauth_code_expiration_invalid` |
| **IndieAuth** | RefreshTokenExpirationDays <= 0 | error | `indieauth_refresh_expiration_invalid` |
| **IndieAuth** | PendingAuthenticationExpirationMinutes <= 0 | error | `indieauth_pending_expiration_invalid` |
| **Talos** | BaseUrl is empty | error | `baseurl_missing` |
| **Database** | Connection string missing | error | `database_connection_missing` |
| **Database** | Cannot connect to database | error | `database_connection_failed` |

### Severity Levels

- **error**: Talos will not function correctly. Must be fixed.
- **warning**: Talos will work but configuration is not recommended.

## API Design

### GET /api/status

Returns the configuration status.

**Response:**
```json
{
  "configured": false,
  "environment": "Development",
  "issues": [
    {
      "code": "github_client_id_missing",
      "category": "GitHub",
      "message": "GitHub Client ID is not configured",
      "severity": "error"
    },
    {
      "code": "github_client_secret_missing", 
      "category": "GitHub",
      "message": "GitHub Client Secret is not configured",
      "severity": "error"
    }
  ]
}
```

When fully configured:
```json
{
  "configured": true,
  "environment": "Production",
  "issues": []
}
```

## Implementation

### StatusController.cs

```csharp
[ApiController]
[Route("api/[controller]")]
public class StatusController : ControllerBase
{
    private readonly IConfiguration _configuration;
    private readonly TalosDbContext _dbContext;
    private readonly IWebHostEnvironment _environment;

    public StatusController(
        IConfiguration configuration,
        TalosDbContext dbContext,
        IWebHostEnvironment environment)
    {
        _configuration = configuration;
        _dbContext = dbContext;
        _environment = environment;
    }

    [HttpGet]
    public async Task<IActionResult> GetStatus()
    {
        var issues = new List<ConfigurationIssue>();

        // GitHub checks
        var github = _configuration.GetSection("GitHub");
        if (string.IsNullOrEmpty(github["ClientId"]))
            issues.Add(new("github_client_id_missing", "GitHub", "GitHub Client ID is not configured", "error"));
        if (string.IsNullOrEmpty(github["ClientSecret"]))
            issues.Add(new("github_client_secret_missing", "GitHub", "GitHub Client Secret is not configured", "error"));

        // JWT checks
        var jwt = _configuration.GetSection("Jwt");
        var secretKey = jwt["SecretKey"] ?? "";
        if (string.IsNullOrEmpty(secretKey))
            issues.Add(new("jwt_secret_key_missing", "JWT", "JWT Secret Key is not configured", "error"));
        else if (secretKey.Length < 32)
            issues.Add(new("jwt_secret_key_too_short", "JWT", "JWT Secret Key must be at least 32 characters", "error"));
        if (string.IsNullOrEmpty(jwt["Issuer"]))
            issues.Add(new("jwt_issuer_missing", "JWT", "JWT Issuer is not configured", "error"));
        if (string.IsNullOrEmpty(jwt["Audience"]))
            issues.Add(new("jwt_audience_missing", "JWT", "JWT Audience is not configured", "error"));
        if (int.TryParse(jwt["AccessTokenExpirationMinutes"], out var expMin) && expMin <= 0)
            issues.Add(new("jwt_expiration_invalid", "JWT", "JWT Access Token Expiration must be greater than 0", "error"));

        // IndieAuth checks
        var indieAuth = _configuration.GetSection("IndieAuth");
        if (int.TryParse(indieAuth["AuthorizationCodeExpirationMinutes"], out var codeExp) && codeExp <= 0)
            issues.Add(new("indieauth_code_expiration_invalid", "IndieAuth", "Authorization Code Expiration must be greater than 0", "error"));
        if (int.TryParse(indieAuth["RefreshTokenExpirationDays"], out var refreshExp) && refreshExp <= 0)
            issues.Add(new("indieauth_refresh_expiration_invalid", "IndieAuth", "Refresh Token Expiration must be greater than 0", "error"));
        if (int.TryParse(indieAuth["PendingAuthenticationExpirationMinutes"], out var pendingExp) && pendingExp <= 0)
            issues.Add(new("indieauth_pending_expiration_invalid", "IndieAuth", "Pending Authentication Expiration must be greater than 0", "error"));

        // Talos checks
        var talos = _configuration.GetSection("Talos");
        if (string.IsNullOrEmpty(talos["BaseUrl"]))
            issues.Add(new("baseurl_missing", "Talos", "Base URL is not configured", "error"));

        // Database checks
        var connectionString = _configuration.GetConnectionString("DefaultConnection");
        if (string.IsNullOrEmpty(connectionString))
        {
            issues.Add(new("database_connection_missing", "Database", "Database connection string is not configured", "error"));
        }
        else
        {
            try
            {
                await _dbContext.Database.CanConnectAsync();
            }
            catch
            {
                issues.Add(new("database_connection_failed", "Database", "Cannot connect to the database", "error"));
            }
        }

        var hasErrors = issues.Any(i => i.Severity == "error");

        return Ok(new
        {
            configured = !hasErrors,
            environment = _environment.EnvironmentName,
            issues
        });
    }
}

public record ConfigurationIssue(string Code, string Category, string Message, string Severity);
```

## Vue.js Components

### HomeView.vue

The landing page that determines which content to show.

```vue
<template>
  <div class="min-h-screen bg-gray-50">
    <!-- Loading -->
    <div v-if="loading" class="flex items-center justify-center min-h-screen">
      <div class="animate-spin rounded-full h-12 w-12 border-b-2 border-indigo-600"></div>
    </div>

    <!-- Setup Guide (not configured) -->
    <SetupGuide v-else-if="!status?.configured" :issues="status?.issues || []" />

    <!-- Project Info (configured) -->
    <ProjectInfo v-else />
  </div>
</template>

<script setup lang="ts">
import { ref, onMounted } from 'vue'
import SetupGuide from '../components/SetupGuide.vue'
import ProjectInfo from '../components/ProjectInfo.vue'

interface ConfigurationIssue {
  code: string
  category: string
  message: string
  severity: 'error' | 'warning'
}

interface Status {
  configured: boolean
  environment: string
  issues: ConfigurationIssue[]
}

const loading = ref(true)
const status = ref<Status | null>(null)

async function loadStatus() {
  try {
    const response = await fetch('/api/status')
    status.value = await response.json()
  } catch (e) {
    console.error('Failed to load status:', e)
  } finally {
    loading.value = false
  }
}

onMounted(loadStatus)
</script>
```

### SetupGuide.vue

Shows configuration issues grouped by category with expandable instructions.

```vue
<template>
  <div class="max-w-3xl mx-auto py-12 px-4">
    <div class="text-center mb-8">
      <div class="w-16 h-16 bg-yellow-100 rounded-full flex items-center justify-center mx-auto mb-4">
        <svg class="w-8 h-8 text-yellow-600" fill="none" stroke="currentColor" viewBox="0 0 24 24">
          <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" 
            d="M12 9v2m0 4h.01m-6.938 4h13.856c1.54 0 2.502-1.667 1.732-3L13.732 4c-.77-1.333-2.694-1.333-3.464 0L3.34 16c-.77 1.333.192 3 1.732 3z" />
        </svg>
      </div>
      <h1 class="text-2xl font-bold text-gray-900">Talos Setup Required</h1>
      <p class="text-gray-600 mt-2">Complete the following configuration steps to get started</p>
    </div>

    <!-- Issues grouped by category -->
    <div class="space-y-4">
      <div v-for="(categoryIssues, category) in groupedIssues" :key="category" 
           class="bg-white rounded-lg shadow-md overflow-hidden">
        <button @click="toggleCategory(category)" 
                class="w-full px-6 py-4 flex items-center justify-between bg-gray-50 hover:bg-gray-100">
          <div class="flex items-center gap-3">
            <span class="w-6 h-6 rounded-full flex items-center justify-center"
                  :class="hasErrors(categoryIssues) ? 'bg-red-100 text-red-600' : 'bg-yellow-100 text-yellow-600'">
              {{ categoryIssues.length }}
            </span>
            <span class="font-semibold text-gray-900">{{ category }}</span>
          </div>
          <svg class="w-5 h-5 text-gray-500 transition-transform" 
               :class="{ 'rotate-180': expandedCategories.includes(category) }"
               fill="none" stroke="currentColor" viewBox="0 0 24 24">
            <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M19 9l-7 7-7-7" />
          </svg>
        </button>
        
        <div v-if="expandedCategories.includes(category)" class="px-6 py-4 border-t">
          <ul class="space-y-3">
            <li v-for="issue in categoryIssues" :key="issue.code" class="flex items-start gap-3">
              <span class="mt-0.5 w-2 h-2 rounded-full flex-shrink-0"
                    :class="issue.severity === 'error' ? 'bg-red-500' : 'bg-yellow-500'"></span>
              <div>
                <p class="text-gray-700">{{ issue.message }}</p>
                <p class="text-sm text-gray-500 mt-1">{{ getInstructions(issue.code) }}</p>
              </div>
            </li>
          </ul>
          
          <!-- Category-specific setup instructions -->
          <div class="mt-4 p-4 bg-gray-50 rounded-lg">
            <h4 class="font-medium text-gray-900 mb-2">Configuration</h4>
            <pre class="text-sm bg-gray-900 text-gray-100 p-4 rounded overflow-x-auto"><code>{{ getConfigExample(category) }}</code></pre>
          </div>
        </div>
      </div>
    </div>

    <!-- Help link -->
    <div class="mt-8 text-center">
      <p class="text-gray-600">
        Need help? Check the 
        <a href="https://github.com/yourusername/talos#readme" 
           class="text-indigo-600 hover:text-indigo-800">documentation</a>.
      </p>
    </div>
  </div>
</template>
```

### ProjectInfo.vue

Informational page shown when Talos is properly configured.

```vue
<template>
  <div class="max-w-4xl mx-auto py-12 px-4">
    <!-- Hero -->
    <div class="text-center mb-12">
      <h1 class="text-4xl font-bold text-gray-900 mb-4">Talos</h1>
      <p class="text-xl text-gray-600">An IndieAuth Authorization Server</p>
      <div class="mt-4 flex items-center justify-center gap-2">
        <span class="inline-flex items-center px-3 py-1 rounded-full text-sm font-medium bg-green-100 text-green-800">
          <span class="w-2 h-2 bg-green-500 rounded-full mr-2"></span>
          Configured & Ready
        </span>
      </div>
    </div>

    <!-- What is IndieAuth -->
    <section class="mb-12">
      <h2 class="text-2xl font-bold text-gray-900 mb-4">What is IndieAuth?</h2>
      <p class="text-gray-600 mb-4">
        IndieAuth is a decentralized identity protocol that allows you to sign in to websites 
        using your own domain name. Instead of relying on centralized identity providers, 
        you control your identity through your personal website.
      </p>
      <p class="text-gray-600">
        Talos implements the IndieAuth specification, acting as an authorization server that 
        authenticates you via third-party identity providers (like GitHub) discovered from 
        your website using RelMeAuth.
      </p>
    </section>

    <!-- How it works -->
    <section class="mb-12">
      <h2 class="text-2xl font-bold text-gray-900 mb-4">How It Works</h2>
      <div class="grid md:grid-cols-3 gap-6">
        <div class="bg-white p-6 rounded-lg shadow-md">
          <div class="w-10 h-10 bg-indigo-100 rounded-lg flex items-center justify-center mb-4">
            <span class="text-indigo-600 font-bold">1</span>
          </div>
          <h3 class="font-semibold text-gray-900 mb-2">Enter Your URL</h3>
          <p class="text-gray-600 text-sm">
            When signing in to an IndieAuth-compatible app, enter your personal website URL.
          </p>
        </div>
        <div class="bg-white p-6 rounded-lg shadow-md">
          <div class="w-10 h-10 bg-indigo-100 rounded-lg flex items-center justify-center mb-4">
            <span class="text-indigo-600 font-bold">2</span>
          </div>
          <h3 class="font-semibold text-gray-900 mb-2">Verify Identity</h3>
          <p class="text-gray-600 text-sm">
            Talos discovers your identity providers via rel="me" links and verifies you through GitHub.
          </p>
        </div>
        <div class="bg-white p-6 rounded-lg shadow-md">
          <div class="w-10 h-10 bg-indigo-100 rounded-lg flex items-center justify-center mb-4">
            <span class="text-indigo-600 font-bold">3</span>
          </div>
          <h3 class="font-semibold text-gray-900 mb-2">Authorize Access</h3>
          <p class="text-gray-600 text-sm">
            Review the requested permissions and authorize the application to access your identity.
          </p>
        </div>
      </div>
    </section>

    <!-- Setup your website -->
    <section class="mb-12">
      <h2 class="text-2xl font-bold text-gray-900 mb-4">Configure Your Website</h2>
      <p class="text-gray-600 mb-4">
        To use Talos as your IndieAuth server, add these tags to your website's HTML:
      </p>
      <pre class="bg-gray-900 text-gray-100 p-4 rounded-lg overflow-x-auto text-sm"><code>&lt;link rel="authorization_endpoint" href="{{ baseUrl }}/auth"&gt;
&lt;link rel="token_endpoint" href="{{ baseUrl }}/token"&gt;

&lt;!-- Link to your GitHub profile --&gt;
&lt;a href="https://github.com/yourusername" rel="me"&gt;GitHub&lt;/a&gt;</code></pre>
      <p class="text-gray-600 mt-4">
        Make sure your GitHub profile links back to your website for verification.
      </p>
    </section>

    <!-- Links -->
    <section>
      <h2 class="text-2xl font-bold text-gray-900 mb-4">Resources</h2>
      <ul class="space-y-2">
        <li>
          <a href="https://indieauth.spec.indieweb.org/" class="text-indigo-600 hover:text-indigo-800">
            IndieAuth Specification →
          </a>
        </li>
        <li>
          <a href="https://indieweb.org/RelMeAuth" class="text-indigo-600 hover:text-indigo-800">
            RelMeAuth Documentation →
          </a>
        </li>
        <li>
          <a href="https://github.com/yourusername/talos" class="text-indigo-600 hover:text-indigo-800">
            Talos on GitHub →
          </a>
        </li>
      </ul>
    </section>
  </div>
</template>
```

## Router Update

```typescript
// router/index.ts
import HomeView from '../views/HomeView.vue'

const router = createRouter({
  history: createWebHistory(import.meta.env.BASE_URL),
  routes: [
    {
      path: '/',
      name: 'home',
      component: HomeView
    },
    {
      path: '/select-provider',
      name: 'provider-select',
      component: ProviderSelectView
    },
    // ... other routes
  ]
})
```

## Setup Instructions Content

The `SetupGuide.vue` component provides detailed instructions for each issue:

### GitHub Configuration

```json
{
  "GitHub": {
    "ClientId": "your-github-client-id",
    "ClientSecret": "your-github-client-secret"
  }
}
```

**Steps:**
1. Go to GitHub Settings → Developer settings → OAuth Apps
2. Click "New OAuth App"
3. Set Authorization callback URL to `{baseUrl}/callback/github`
4. Copy Client ID and Client Secret to your configuration

### JWT Configuration

```json
{
  "Jwt": {
    "Issuer": "https://your-domain.com/",
    "Audience": "https://your-domain.com/",
    "SecretKey": "generate-a-random-32-character-or-longer-key",
    "AccessTokenExpirationMinutes": 15
  }
}
```

**Steps:**
1. Generate a secure random key (at least 32 characters)
2. Set Issuer and Audience to your Talos instance URL
3. Configure token expiration as needed

### Database Configuration

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Data Source=talos.db"
  }
}
```

**Steps:**
1. Ensure the connection string points to a valid SQLite database path
2. The application will create the database automatically on first run
3. Ensure the application has write permissions to the database directory

### Talos Configuration

```json
{
  "Talos": {
    "BaseUrl": "https://your-domain.com"
  }
}
```

**Steps:**
1. Set BaseUrl to the public URL where Talos is hosted
2. Include the protocol (https://) but not a trailing slash

