<template>
  <div class="max-w-4xl mx-auto py-12 px-4">

    <img src="/talos-logo.png" alt="Talos Illustration" class="mx-auto mb-12 w-64"/>
    
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
        To use Talos as your IndieAuth server, add these tags to your website's HTML <code class="bg-gray-100 px-1 rounded">&lt;head&gt;</code>:
      </p>
      <pre class="bg-gray-900 text-gray-100 p-4 rounded-lg overflow-x-auto text-sm"><code>&lt;link rel="authorization_endpoint" href="{{ baseUrl }}/auth"&gt;
&lt;link rel="token_endpoint" href="{{ baseUrl }}/token"&gt;

&lt;!-- Link to your GitHub profile with rel="me" --&gt;
&lt;a href="https://github.com/yourusername" rel="me"&gt;GitHub&lt;/a&gt;</code></pre>
      <p class="text-gray-600 mt-4">
        <strong>Important:</strong> Your GitHub profile must also link back to your website for verification to work.
      </p>
      
      <!-- Personal Server Mode note -->
      <div class="mt-4 p-4 bg-blue-50 rounded-lg border border-blue-100">
        <p class="text-sm text-blue-800">
          <strong>Self-hosted tip:</strong> Server operators can optionally restrict authentication to specific websites 
          using the <code class="bg-blue-100 px-1 rounded">AllowedProfileHosts</code> configuration setting.
        </p>
      </div>
    </section>

    <!-- Endpoints -->
    <section class="mb-12">
      <h2 class="text-2xl font-bold text-gray-900 mb-4">API Endpoints</h2>
      <div class="bg-white rounded-lg shadow-md overflow-hidden">
        <table class="w-full text-left">
          <thead class="bg-gray-50 border-b border-gray-200">
            <tr>
              <th class="px-6 py-3 text-sm font-semibold text-gray-900">Endpoint</th>
              <th class="px-6 py-3 text-sm font-semibold text-gray-900">Description</th>
            </tr>
          </thead>
          <tbody class="divide-y divide-gray-200">
            <tr>
              <td class="px-6 py-4"><code class="text-sm bg-gray-100 px-2 py-1 rounded">GET /auth</code></td>
              <td class="px-6 py-4 text-gray-600 text-sm">Authorization endpoint (IndieAuth)</td>
            </tr>
            <tr>
              <td class="px-6 py-4"><code class="text-sm bg-gray-100 px-2 py-1 rounded">POST /token</code></td>
              <td class="px-6 py-4 text-gray-600 text-sm">Token endpoint (code exchange, refresh)</td>
            </tr>
            <tr>
              <td class="px-6 py-4"><code class="text-sm bg-gray-100 px-2 py-1 rounded">POST /token/introspect</code></td>
              <td class="px-6 py-4 text-gray-600 text-sm">Token introspection (RFC 7662)</td>
            </tr>
            <tr>
              <td class="px-6 py-4"><code class="text-sm bg-gray-100 px-2 py-1 rounded">POST /token/revoke</code></td>
              <td class="px-6 py-4 text-gray-600 text-sm">Token revocation (RFC 7009)</td>
            </tr>
            <tr>
              <td class="px-6 py-4"><code class="text-sm bg-gray-100 px-2 py-1 rounded">GET /.well-known/oauth-authorization-server</code></td>
              <td class="px-6 py-4 text-gray-600 text-sm">Server metadata discovery</td>
            </tr>
          </tbody>
        </table>
      </div>
    </section>

    <!-- Links -->
    <section>
      <h2 class="text-2xl font-bold text-gray-900 mb-4">Resources</h2>
      <ul class="space-y-3">
        <li>
          <a href="https://indieauth.spec.indieweb.org/" target="_blank" 
             class="text-indigo-600 hover:text-indigo-800 flex items-center gap-2">
            <svg class="w-4 h-4" fill="none" stroke="currentColor" viewBox="0 0 24 24">
              <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" 
                d="M10 6H6a2 2 0 00-2 2v10a2 2 0 002 2h10a2 2 0 002-2v-4M14 4h6m0 0v6m0-6L10 14" />
            </svg>
            IndieAuth Specification
          </a>
        </li>
        <li>
          <a href="https://indieweb.org/RelMeAuth" target="_blank"
             class="text-indigo-600 hover:text-indigo-800 flex items-center gap-2">
            <svg class="w-4 h-4" fill="none" stroke="currentColor" viewBox="0 0 24 24">
              <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" 
                d="M10 6H6a2 2 0 00-2 2v10a2 2 0 002 2h10a2 2 0 002-2v-4M14 4h6m0 0v6m0-6L10 14" />
            </svg>
            RelMeAuth Documentation
          </a>
        </li>
        <li>
          <a href="https://github.com/myquay/talos" target="_blank"
             class="text-indigo-600 hover:text-indigo-800 flex items-center gap-2">
            <svg class="w-4 h-4" fill="currentColor" viewBox="0 0 24 24">
              <path fill-rule="evenodd" clip-rule="evenodd" 
                d="M12 2C6.477 2 2 6.484 2 12.017c0 4.425 2.865 8.18 6.839 9.504.5.092.682-.217.682-.483 0-.237-.008-.868-.013-1.703-2.782.605-3.369-1.343-3.369-1.343-.454-1.158-1.11-1.466-1.11-1.466-.908-.62.069-.608.069-.608 1.003.07 1.531 1.032 1.531 1.032.892 1.53 2.341 1.088 2.91.832.092-.647.35-1.088.636-1.338-2.22-.253-4.555-1.113-4.555-4.951 0-1.093.39-1.988 1.029-2.688-.103-.253-.446-1.272.098-2.65 0 0 .84-.27 2.75 1.026A9.564 9.564 0 0112 6.844c.85.004 1.705.115 2.504.337 1.909-1.296 2.747-1.027 2.747-1.027.546 1.379.202 2.398.1 2.651.64.7 1.028 1.595 1.028 2.688 0 3.848-2.339 4.695-4.566 4.943.359.309.678.92.678 1.855 0 1.338-.012 2.419-.012 2.747 0 .268.18.58.688.482A10.019 10.019 0 0022 12.017C22 6.484 17.522 2 12 2z" />
            </svg>
            Talos on GitHub
          </a>
        </li>
      </ul>
    </section>

    <!-- Footer -->
    <footer class="mt-16 pt-8 border-t border-gray-200 text-center text-gray-500 text-sm">
      <p>Talos is an open-source IndieAuth server.</p>
    </footer>
  </div>
</template>

<script setup lang="ts">
defineProps<{
  baseUrl: string
}>()
</script>

