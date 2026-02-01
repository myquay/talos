<template>
  <div class="min-h-screen bg-gray-50">
    <!-- Loading -->
    <div v-if="loading" class="flex items-center justify-center min-h-screen">
      <div class="animate-spin rounded-full h-12 w-12 border-b-2 border-indigo-600"></div>
    </div>

    <!-- Setup Guide (not configured) -->
    <SetupGuide v-else-if="!status?.configured" :issues="status?.issues || []" />

    <!-- Project Info (configured) -->
    <ProjectInfo v-else :baseUrl="baseUrl" />
  </div>
</template>

<script setup lang="ts">
import { ref, computed, onMounted } from 'vue'
import SetupGuide from '../components/SetupGuide.vue'
import ProjectInfo from '../components/ProjectInfo.vue'

interface ConfigurationIssue {
  code: string
  category: string
  message: string
  severity: 'error' | 'warning'
}

interface Status {
  configured: boolean
  environment: string
  issues: ConfigurationIssue[]
}

const loading = ref(true)
const status = ref<Status | null>(null)

const baseUrl = computed(() => {
  return window.location.origin
})

async function loadStatus() {
  try {
    const response = await fetch('/api/status')
    status.value = await response.json()
  } catch (e) {
    console.error('Failed to load status:', e)
  } finally {
    loading.value = false
  }
}

onMounted(loadStatus)
</script>

