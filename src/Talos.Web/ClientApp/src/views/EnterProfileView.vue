<template>
  <div class="min-h-screen bg-gray-100 flex items-center justify-center px-4">
    <div class="max-w-md w-full bg-white rounded-lg shadow-lg p-8">
      <h1 class="text-2xl font-bold text-gray-900 text-center mb-2">Sign In</h1>
      <p class="text-gray-600 text-center mb-6">
        Enter your profile URL to continue
      </p>

      <ClientCard :client="client" />

      <form @submit.prevent="submit" class="mt-6 space-y-4">
        <div>
          <label for="profile-url" class="block text-sm font-medium text-gray-700 mb-1">
            Your profile URL
          </label>
          <input
            id="profile-url"
            v-model="profileUrl"
            type="url"
            placeholder="https://yoursite.example.com/"
            required
            class="w-full px-3 py-2 border border-gray-300 rounded-md shadow-sm focus:outline-none focus:ring-2 focus:ring-indigo-500 focus:border-indigo-500"
          />
        </div>

        <p v-if="error" class="text-sm text-red-600">{{ error }}</p>

        <button
          type="submit"
          class="w-full bg-indigo-600 text-white py-2 px-4 rounded-md hover:bg-indigo-700 transition-colors"
        >
          Continue
        </button>
      </form>

      <div class="mt-6 text-center text-sm text-gray-500">
        <p>Your profile URL is used to discover your identity provider.</p>
      </div>
    </div>
  </div>
</template>

<script setup lang="ts">
import { ref, computed } from 'vue'
import { useRoute } from 'vue-router'
import ClientCard from '../components/ClientCard.vue'

const route = useRoute()
const profileUrl = ref('')
const error = ref('')

const client = computed(() => ({
  clientId: (route.query.client_id as string) ?? '',
  name: (route.query.client_name as string) ?? new URL(route.query.client_id as string).host,
  url: (route.query.client_id as string) ?? '',
  logoUrl: (route.query.client_logo as string) ?? undefined,
}))

function submit() {
  const url = profileUrl.value.trim()
  if (!url) {
    error.value = 'Please enter your profile URL.'
    return
  }

  try {
    new URL(url)
  } catch {
    error.value = 'Please enter a valid URL (e.g. https://yoursite.example.com/).'
    return
  }

  error.value = ''

  // Rebuild original auth params, stripping display-only keys
  const params = new URLSearchParams()
  for (const [key, value] of Object.entries(route.query)) {
    if (key !== 'client_name' && key !== 'client_logo' && typeof value === 'string') {
      params.set(key, value)
    }
  }
  params.set('me', url)

  // Redirect back to /auth — the existing flow handles the rest
  window.location.href = `/auth?${params.toString()}`
}
</script>
