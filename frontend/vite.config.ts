import react from '@vitejs/plugin-react'
import { defineConfig } from 'vite'

// The SPA calls the API on relative paths; dev-proxy them to the .NET backend.
export default defineConfig({
  plugins: [react()],
  server: {
    port: 5173,
    proxy: {
      '/api': { target: 'http://localhost:5080', changeOrigin: true },
      '/hangfire': { target: 'http://localhost:5080', changeOrigin: true },
    },
  },
})
