<template>
  <div class="flex items-center p-4 bg-gray-50 rounded-lg">
    <div class="w-12 h-12 flex-shrink-0 flex items-center justify-center bg-white rounded-lg shadow-sm mr-4 overflow-hidden">
      <img 
        v-if="client.logoUrl && !logoError" 
        :src="client.logoUrl" 
        :alt="client.name"
        class="w-10 h-10 object-contain rounded"
        @error="logoError = true"
      />
      <span v-else class="text-2xl">🌐</span>
    </div>
    <div class="min-w-0">
      <div class="font-semibold text-gray-900 truncate">{{ client.name }}</div>
      <a 
        :href="client.clientId" 
        target="_blank" 
        rel="noopener noreferrer"
        class="text-sm text-indigo-600 hover:underline truncate block"
        :title="client.clientId"
      >
        {{ client.clientId }}
      </a>
    </div>
  </div>
</template>

<script setup lang="ts">
import { ref } from 'vue'

interface Client {
  clientId: string
  name: string
  url: string
  logoUrl?: string
}

defineProps<{
  client: Client
}>()

const logoError = ref(false)
</script>

