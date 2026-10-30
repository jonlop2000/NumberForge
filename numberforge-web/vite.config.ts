import react from '@vitejs/plugin-react'
import { defineConfig } from 'vitest/config'

// https://vite.dev/config/
export default defineConfig({
  plugins: [react()],
  test: {
    // Supply a browser-like DOM so component tests can render elements and query them.
    environment: 'jsdom',
  },
  server: {
    proxy: {
      '/api': 'http://localhost:5100',
    },
  },
})
