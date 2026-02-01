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
        <button @click="toggleCategory(category as string)" 
                class="w-full px-6 py-4 flex items-center justify-between bg-gray-50 hover:bg-gray-100 transition-colors">
          <div class="flex items-center gap-3">
            <span class="w-6 h-6 rounded-full flex items-center justify-center text-sm font-medium"
                  :class="hasErrors(categoryIssues) ? 'bg-red-100 text-red-600' : 'bg-yellow-100 text-yellow-600'">
              {{ categoryIssues.length }}
            </span>
            <span class="font-semibold text-gray-900">{{ category }}</span>
          </div>
          <svg class="w-5 h-5 text-gray-500 transition-transform duration-200" 
               :class="{ 'rotate-180': expandedCategories.includes(category as string) }"
               fill="none" stroke="currentColor" viewBox="0 0 24 24">
            <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M19 9l-7 7-7-7" />
          </svg>
        </button>
        
        <div v-if="expandedCategories.includes(category as string)" class="px-6 py-4 border-t border-gray-200">
          <ul class="space-y-3">
            <li v-for="issue in categoryIssues" :key="issue.code" class="flex items-start gap-3">
              <span class="mt-1.5 w-2 h-2 rounded-full flex-shrink-0"
                    :class="issue.severity === 'error' ? 'bg-red-500' : 'bg-yellow-500'"></span>
              <div>
                <p class="text-gray-700">{{ issue.message }}</p>
                <p class="text-sm text-gray-500 mt-1">{{ getInstructions(issue.code) }}</p>
              </div>
            </li>
          </ul>
          
          <!-- Category-specific setup instructions -->
          <div class="mt-4 p-4 bg-gray-50 rounded-lg">
            <h4 class="font-medium text-gray-900 mb-2">Add to appsettings.json:</h4>
            <pre class="text-sm bg-gray-900 text-gray-100 p-4 rounded-lg overflow-x-auto"><code>{{ getConfigExample(category as string) }}</code></pre>
          </div>

          <!-- Category-specific help -->
          <div v-if="getCategoryHelp(category as string)" class="mt-4 p-4 bg-blue-50 rounded-lg border border-blue-100">
            <h4 class="font-medium text-blue-900 mb-2">Setup Instructions:</h4>
            <div class="text-sm text-blue-800 space-y-2" v-html="getCategoryHelp(category as string)"></div>
          </div>
        </div>
      </div>
    </div>

    <!-- Help link -->
    <div class="mt-8 text-center">
      <p class="text-gray-600">
        Need help? Check the 
        <a href="https://github.com/myquay/talos#readme" 
           class="text-indigo-600 hover:text-indigo-800 underline">documentation</a>.
      </p>
    </div>
  </div>
</template>

<script setup lang="ts">
import { ref, computed } from 'vue'

interface ConfigurationIssue {
  code: string
  category: string
  message: string
  severity: 'error' | 'warning'
}

const props = defineProps<{
  issues: ConfigurationIssue[]
}>()

const expandedCategories = ref<string[]>([])

// Auto-expand first category with issues
if (props.issues.length > 0 && props.issues[0]) {
  const firstCategory = props.issues[0].category
  expandedCategories.value.push(firstCategory)
}

const groupedIssues = computed(() => {
  const groups: Record<string, ConfigurationIssue[]> = {}
  for (const issue of props.issues) {
    if (!groups[issue.category]) {
      groups[issue.category] = []
    }
    groups[issue.category]!.push(issue)
  }
  return groups
})

function toggleCategory(category: string) {
  const index = expandedCategories.value.indexOf(category)
  if (index === -1) {
    expandedCategories.value.push(category)
  } else {
    expandedCategories.value.splice(index, 1)
  }
}

function hasErrors(issues: ConfigurationIssue[]): boolean {
  return issues.some(i => i.severity === 'error')
}

function getInstructions(code: string): string {
  const instructions: Record<string, string> = {
    github_client_id_missing: 'Create a GitHub OAuth App and copy the Client ID',
    github_client_secret_missing: 'Copy the Client Secret from your GitHub OAuth App',
    jwt_secret_key_missing: 'Generate a secure random string of at least 32 characters',
    jwt_secret_key_too_short: 'Your secret key must be at least 32 characters for security',
    jwt_issuer_missing: 'Set to the URL where Talos is hosted (e.g., https://auth.example.com)',
    jwt_audience_missing: 'Usually the same as the Issuer URL',
    jwt_expiration_invalid: 'Must be a positive number (recommended: 15 minutes)',
    indieauth_code_expiration_invalid: 'Must be a positive number (recommended: 10 minutes)',
    indieauth_refresh_expiration_invalid: 'Must be a positive number (recommended: 30 days)',
    indieauth_pending_expiration_invalid: 'Must be a positive number (recommended: 30 minutes)',
    baseurl_missing: 'Set to the public URL where Talos is accessible',
    database_connection_missing: 'Configure a SQLite connection string',
    database_connection_failed: 'Check the database path and file permissions'
  }
  return instructions[code] || ''
}

function getConfigExample(category: string): string {
  const examples: Record<string, string> = {
    GitHub: `"GitHub": {
  "ClientId": "your-github-client-id",
  "ClientSecret": "your-github-client-secret",
  "AuthorizationEndpoint": "https://github.com/login/oauth/authorize",
  "TokenEndpoint": "https://github.com/login/oauth/access_token",
  "UserApiEndpoint": "https://api.github.com/user"
}`,
    JWT: `"Jwt": {
  "Issuer": "https://your-domain.com/",
  "Audience": "https://your-domain.com/",
  "SecretKey": "your-secure-random-key-at-least-32-chars",
  "AccessTokenExpirationMinutes": 15
}`,
    IndieAuth: `"IndieAuth": {
  "AuthorizationCodeExpirationMinutes": 10,
  "RefreshTokenExpirationDays": 30,
  "PendingAuthenticationExpirationMinutes": 30
}`,
    Talos: `"Talos": {
  "BaseUrl": "https://your-domain.com",
  "AllowedProfileHosts": null
}`,
    Database: `"ConnectionStrings": {
  "DefaultConnection": "Data Source=talos.db"
}`
  }
  return examples[category] || ''
}

function getCategoryHelp(category: string): string {
  const help: Record<string, string> = {
    GitHub: `
      <ol class="list-decimal list-inside space-y-1">
        <li>Go to <a href="https://github.com/settings/developers" target="_blank" class="underline">GitHub Developer Settings</a></li>
        <li>Click "OAuth Apps" → "New OAuth App"</li>
        <li>Set the <strong>Authorization callback URL</strong> to: <code class="bg-blue-100 px-1 rounded">{your-base-url}/callback/github</code></li>
        <li>Copy the Client ID and generate a Client Secret</li>
        <li>Add both values to your configuration</li>
      </ol>
    `,
    JWT: `
      <ol class="list-decimal list-inside space-y-1">
        <li>Generate a secure random key using a tool like <code class="bg-blue-100 px-1 rounded">openssl rand -base64 32</code></li>
        <li>Set Issuer and Audience to your Talos instance URL</li>
        <li>AccessTokenExpirationMinutes controls how long tokens are valid (15 minutes recommended)</li>
      </ol>
    `,
    Database: `
      <ol class="list-decimal list-inside space-y-1">
        <li>SQLite is used by default - just specify a file path</li>
        <li>The database will be created automatically on first run</li>
        <li>Ensure the application has write permissions to the database directory</li>
      </ol>
    `,
    Talos: `
      <ol class="list-decimal list-inside space-y-1">
        <li>Set BaseUrl to the public URL where Talos is hosted</li>
        <li>Include the protocol (https://) but no trailing slash</li>
        <li>This URL is used for OAuth callbacks and token issuing</li>
        <li>Optionally set AllowedProfileHosts to an array of hostnames to restrict which websites can authenticate (personal server mode)</li>
      </ol>
    `
  }
  return help[category] || ''
}
</script>

