<template>
  <div class="min-h-screen bg-gray-100 flex items-center justify-center px-4">
    <div class="max-w-md w-full bg-white rounded-lg shadow-lg p-8 text-center">
      <div class="text-red-500 mb-4">
        <svg class="w-16 h-16 mx-auto" fill="none" stroke="currentColor" viewBox="0 0 24 24">
          <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" 
            d="M12 9v2m0 4h.01m-6.938 4h13.856c1.54 0 2.502-1.667 1.732-3L13.732 4c-.77-1.333-2.694-1.333-3.464 0L3.34 16c-.77 1.333.192 3 1.732 3z" />
        </svg>
      </div>
      
      <h1 class="text-2xl font-bold text-gray-900 mb-2">Authentication Error</h1>
      
      <p class="text-gray-600 mb-4">{{ errorDescription }}</p>
      
      <div v-if="errorCode" class="bg-gray-100 rounded p-3 mb-6">
        <span class="text-sm text-gray-500">Error code: </span>
        <span class="text-sm font-mono text-gray-700">{{ errorCode }}</span>
      </div>

      <a 
        v-if="returnUrl"
        :href="returnUrl"
        class="inline-block bg-indigo-600 text-white py-2 px-6 rounded-md hover:bg-indigo-700 transition-colors"
      >
        Return to Application
      </a>
    </div>
  </div>
</template>

<script setup lang="ts">
import { computed } from 'vue'
import { useRoute } from 'vue-router'

const route = useRoute()

const errorCode = computed(() => route.query.error as string || '')
const errorDescription = computed(() => {
  const desc = route.query.error_description as string
  if (desc) return desc
  
  // Default messages for common error codes
  switch (errorCode.value) {
    case 'invalid_request':
      return 'The authorization request was invalid or malformed.'
    case 'unauthorized_client':
      return 'The client is not authorized to request an authorization code.'
    case 'access_denied':
      return 'The resource owner or authorization server denied the request.'
    case 'unsupported_response_type':
      return 'The authorization server does not support this response type.'
    case 'invalid_scope':
      return 'The requested scope is invalid, unknown, or malformed.'
    case 'server_error':
      return 'The authorization server encountered an unexpected condition.'
    case 'temporarily_unavailable':
      return 'The authorization server is temporarily unavailable. Please try again later.'
    case 'provider_not_found':
      return 'No supported identity providers were found on your website.'
    case 'verification_failed':
      return 'We could not verify your identity with the selected provider.'
    default:
      return 'An unexpected error occurred during authentication.'
  }
})

const returnUrl = computed(() => route.query.redirect_uri as string || '')
</script>

