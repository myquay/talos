<template>
  <button
    @click="$emit('select', provider)"
    class="w-full flex items-center p-4 border border-gray-200 rounded-lg hover:border-indigo-500 hover:bg-indigo-50 transition-colors"
  >
    <div class="w-10 h-10 flex items-center justify-center bg-gray-100 rounded-full mr-4">
      <img 
        v-if="provider.iconUrl" 
        :src="provider.iconUrl" 
        :alt="provider.name"
        class="w-6 h-6"
      />
      <span v-else class="text-xl">{{ providerIcon }}</span>
    </div>
    <div class="flex-1 text-left">
      <div class="font-semibold text-gray-900">{{ provider.name }}</div>
      <div class="text-sm text-gray-500">{{ provider.profileUrl }}</div>
    </div>
    <svg class="w-5 h-5 text-gray-400" fill="none" stroke="currentColor" viewBox="0 0 24 24">
      <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M9 5l7 7-7 7" />
    </svg>
  </button>
</template>

<script setup lang="ts">
import { computed } from 'vue'

interface Provider {
  type: string
  name: string
  profileUrl: string
  iconUrl?: string
}

const props = defineProps<{
  provider: Provider
}>()

defineEmits<{
  (e: 'select', provider: Provider): void
}>()

const providerIcon = computed(() => {
  switch (props.provider.type.toLowerCase()) {
    case 'github':
      return '🐙'
    case 'twitter':
    case 'x':
      return '🐦'
    case 'mastodon':
      return '🐘'
    case 'email':
      return '✉️'
    default:
      return '🔐'
  }
})
</script>

