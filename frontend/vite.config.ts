import { defineConfig } from 'vitest/config'
import react from '@vitejs/plugin-react'

// https://vite.dev/config/
export default defineConfig({
  plugins: [react()],
  test: {
    environment: 'jsdom',
    globals: true,
    setupFiles: ['./src/test/setup.ts'],
    // Node 22+ ships a global `localStorage`/`sessionStorage` that's non-functional without
    // `--localstorage-file` and shadows jsdom's own per-window storage — disable it so
    // AuthContext's localStorage calls hit jsdom's real (in-memory, per-test) implementation.
    execArgv: ['--no-experimental-webstorage'],
  },
})
