import { fileURLToPath, URL } from 'node:url'
import tailwindcss from '@tailwindcss/vite'
import vue from '@vitejs/plugin-vue'
import { defineConfig } from 'vitest/config'

// In development the API proxies Vite (YARP), so the browser only ever talks to the API origin. The AppHost starts this
// server with the port in PORT (AddViteApp); the built files land in dist/, which the API serves in static mode.
export default defineConfig({
  plugins: [vue(), tailwindcss()],
  resolve: { alias: { '@': fileURLToPath(new URL('./src', import.meta.url)) } },
  server: { host: true, port: Number(process.env.PORT ?? 5173), strictPort: true },
  build: { outDir: 'dist' },
  test: { environment: 'jsdom', include: ['src/**/*.test.ts'] },
})
