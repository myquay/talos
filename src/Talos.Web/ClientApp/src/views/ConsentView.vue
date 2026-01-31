<template>
  <div class="min-h-screen bg-gray-100 flex items-center justify-center px-4">
    <div class="max-w-md w-full bg-white rounded-lg shadow-lg p-8">
      <h1 class="text-2xl font-bold text-gray-900 text-center mb-2">Authorize Application</h1>
      
      <div v-if="loading" class="flex justify-center py-8">
        <div class="animate-spin rounded-full h-8 w-8 border-b-2 border-indigo-600"></div>
      </div>

      <div v-else-if="error" class="text-red-600 text-center py-4">
        {{ error }}
      </div>

      <div v-else>
        <ClientCard :client="client" />

        <div class="mt-6">
          <p class="text-sm text-gray-600 mb-3">
            <span class="font-semibold">{{ client.name }}</span> is requesting access to:
          </p>
          <ScopeList :scopes="scopes" />
        </div>

        <div class="mt-6 space-y-3">
          <button
            @click="approve"
            class="w-full bg-indigo-600 text-white py-2 px-4 rounded-md hover:bg-indigo-700 transition-colors"
          >
            Authorize
          </button>
          <button
            @click="deny"
            class="w-full bg-gray-200 text-gray-700 py-2 px-4 rounded-md hover:bg-gray-300 transition-colors"
          >
            Deny
          </button>
        </div>

        <p class="mt-4 text-xs text-gray-500 text-center">
          You are signing in as <span class="font-semibold">{{ profileUrl }}</span>
        </p>
      </div>
    </div>
  </div>
</template>

<script setup lang="ts">
import { ref, onMounted } from 'vue'
import { useRoute } from 'vue-router'
import ClientCard from '../components/ClientCard.vue'
import ScopeList from '../components/ScopeList.vue'
import { getConsentInfo, submitConsent } from '../api/auth'

interface Client {
  clientId: string
  name: string
  url: string
  logoUrl?: string
}

const route = useRoute()
const client = ref<Client>({ clientId: '', name: '', url: '' })
const scopes = ref<string[]>([])
const profileUrl = ref('')
const loading = ref(true)
const error = ref('')

const approve = async () => {
  try {
    const sessionId = route.query.session_id as string
    const result = await submitConsent(sessionId, true)
    if (result.redirectUrl) {
      window.location.href = result.redirectUrl
    }
  } catch (e) {
    error.value = 'Failed to process authorization. Please try again.'
  }
}

const deny = async () => {
  try {
    const sessionId = route.query.session_id as string
    const result = await submitConsent(sessionId, false)
    if (result.redirectUrl) {
      window.location.href = result.redirectUrl
    }
  } catch (e) {
    error.value = 'Failed to process authorization. Please try again.'
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

    const data = await getConsentInfo(sessionId)
    client.value = data.client
    scopes.value = data.scopes
    profileUrl.value = data.profileUrl
  } catch (e) {
    error.value = 'Failed to load authorization details.'
  } finally {
    loading.value = false
  }
})
</script>

