import { createRouter, createWebHistory } from 'vue-router'
import ProviderSelectView from '../views/ProviderSelectView.vue'
import ConsentView from '../views/ConsentView.vue'
import ErrorView from '../views/ErrorView.vue'

const router = createRouter({
  history: createWebHistory(import.meta.env.BASE_URL),
  routes: [
    {
      path: '/select-provider',
      name: 'provider-select',
      component: ProviderSelectView
    },
    {
      path: '/consent',
      name: 'consent',
      component: ConsentView
    },
    {
      path: '/error',
      name: 'error',
      component: ErrorView
    },
    {
      path: '/',
      redirect: '/select-provider'
    }
  ]
})

export default router

