import { createRouter, createWebHistory } from 'vue-router'
import HomeView from '../views/HomeView.vue'
import ProviderSelectView from '../views/ProviderSelectView.vue'
import ConsentView from '../views/ConsentView.vue'
import ErrorView from '../views/ErrorView.vue'
import EnterProfileView from '../views/EnterProfileView.vue'

const router = createRouter({
  history: createWebHistory(import.meta.env.BASE_URL),
  routes: [
    {
      path: '/',
      name: 'home',
      component: HomeView
    },
    {
      path: '/enter-profile',
      name: 'enter-profile',
      component: EnterProfileView
    },
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
    }
  ]
})

export default router

