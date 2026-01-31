<template>
  <div class="min-h-screen bg-gray-100 flex items-center justify-center px-4">
    <div class="max-w-md w-full bg-white rounded-lg shadow-lg p-8">
      <h1 class="text-2xl font-bold text-gray-900 text-center mb-2">Sign In</h1>
      <p class="text-gray-600 text-center mb-6">
        Choose how you want to verify your identity for
        <span class="font-semibold">{{ profileUrl }}</span>
      </p>

      <div v-if="loading" class="flex justify-center py-8">
        <div class="animate-spin rounded-full h-8 w-8 border-b-2 border-indigo-600"></div>
      </div>

      <div v-else-if="error" class="text-red-600 text-center py-4">
        {{ error }}
      </div>

      <div v-else class="space-y-3">
        <ProviderCard
          v-for="provider in providers"
          :key="provider.type"
          :provider="provider"
          @select="selectProvider"
        />
      </div>

      <div class="mt-6 text-center text-sm text-gray-500">
        <p>These providers were discovered from your website's rel="me" links.</p>
      </div>
    </div>
  </div>
</template>

<script setup lang="ts">
import { ref, onMounted } from 'vue'
import { useRoute } from 'vue-router'
import ProviderCard from '../components/ProviderCard.vue'
import { getProviders, selectProvider as apiSelectProvider } from '../api/auth'

interface Provider {
  type: string
  name: string
  profileUrl: string
  iconUrl?: string
}

const route = useRoute()
const providers = ref<Provider[]>([])
const profileUrl = ref('')
const loading = ref(true)
const error = ref('')

const selectProvider = async (provider: Provider) => {
  try {
    const sessionId = route.query.session_id as string
    const result = await apiSelectProvider(sessionId, provider.type)
    if (result.redirectUrl) {
      window.location.href = result.redirectUrl
    }
  } catch (e) {
    error.value = 'Failed to initiate authentication. Please try again.'
  }
}

onMounted(async () => {
  try {
    const sessionId = route.query.session_id as string
    if (!sessionId) {
      error.value = 'Invalid session. Please start the authentication process again.'
      loading.value = false
      return
    }

    const data = await getProviders(sessionId)
    providers.value = data.providers
    profileUrl.value = data.profileUrl
  } catch (e) {
    error.value = 'Failed to load identity providers.'
  } finally {
    loading.value = false
  }
})
</script>

