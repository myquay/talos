<template>
  <div class="flex items-center p-4 bg-gray-50 rounded-lg">
    <div class="w-12 h-12 flex items-center justify-center bg-white rounded-lg shadow-sm mr-4">
      <img 
        v-if="client.logoUrl" 
        :src="client.logoUrl" 
        :alt="client.name"
        class="w-8 h-8"
      />
      <span v-else class="text-2xl">🌐</span>
    </div>
    <div>
      <div class="font-semibold text-gray-900">{{ client.name }}</div>
      <a 
        :href="client.url" 
        target="_blank" 
        class="text-sm text-indigo-600 hover:underline"
      >
        {{ client.url }}
      </a>
    </div>
  </div>
</template>

<script setup lang="ts">
interface Client {
  clientId: string
  name: string
  url: string
  logoUrl?: string
}

defineProps<{
  client: Client
}>()
</script>

