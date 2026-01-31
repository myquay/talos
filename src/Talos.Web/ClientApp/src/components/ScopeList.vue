<template>
  <ul class="space-y-2">
    <li 
      v-for="scope in scopes" 
      :key="scope"
      class="flex items-start"
    >
      <svg class="w-5 h-5 text-green-500 mr-2 mt-0.5" fill="none" stroke="currentColor" viewBox="0 0 24 24">
        <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M5 13l4 4L19 7" />
      </svg>
      <div>
        <span class="font-medium text-gray-900">{{ scopeInfo[scope]?.name || scope }}</span>
        <p class="text-sm text-gray-500">{{ scopeInfo[scope]?.description || '' }}</p>
      </div>
    </li>
  </ul>
</template>

<script setup lang="ts">
defineProps<{
  scopes: string[]
}>()

const scopeInfo: Record<string, { name: string; description: string }> = {
  profile: {
    name: 'Profile',
    description: 'Access your profile URL'
  },
  email: {
    name: 'Email',
    description: 'Access your email address'
  },
  create: {
    name: 'Create',
    description: 'Create new posts on your behalf'
  },
  update: {
    name: 'Update',
    description: 'Update existing posts'
  },
  delete: {
    name: 'Delete',
    description: 'Delete posts'
  },
  media: {
    name: 'Media',
    description: 'Upload media files'
  }
}
</script>

