import { defineConfig } from 'vite'
import vue from '@vitejs/plugin-vue'

// https://vite.dev/config/
export default defineConfig({
  plugins: [vue()],
  build: {
    outDir: '../wwwroot',
    emptyOutDir: true,
  },
  server: {
    proxy: {
      '/api': {
        target: 'https://localhost:5001',
        changeOrigin: true,
        secure: false,
      },
      '/auth': {
        target: 'https://localhost:5001',
        changeOrigin: true,
        secure: false,
      },
      '/callback': {
        target: 'https://localhost:5001',
        changeOrigin: true,
        secure: false,
      },
      '/token': {
        target: 'https://localhost:5001',
        changeOrigin: true,
        secure: false,
      },
      '/.well-known': {
        target: 'https://localhost:5001',
        changeOrigin: true,
        secure: false,
      },
    },
  },
})
