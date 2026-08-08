import { defineConfig } from 'vitest/config'
import react from '@vitejs/plugin-react'
import dts from 'vite-plugin-dts'
import { fileURLToPath } from 'node:url'

export default defineConfig({
  plugins: [react(), dts({ tsconfigPath: './tsconfig.app.json' })],
  test: {
    environment: 'jsdom',
    globals: true,
    setupFiles: ['./src/test/setup.ts'],
  },
  build: {
    cssCodeSplit: false,
    lib: {
      entry: fileURLToPath(new URL('./src/index.ts', import.meta.url)),
      formats: ['es'],
      fileName: 'index',
      cssFileName: 'style',
    },
    rollupOptions: {
      external: ['react', 'react-dom', 'react/jsx-runtime'],
    },
  },
})
