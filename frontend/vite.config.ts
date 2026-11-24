import { fileURLToPath, URL } from 'node:url'
import tailwindcss from '@tailwindcss/vite'
import vue from '@vitejs/plugin-vue'
import type { Plugin } from 'vite'
import { defineConfig } from 'vitest/config'

/**
 * A page opened on Vite's own port gets index.html for its /api calls too, and breaks. A browser that comes here
 * directly, without the X-Forwarded-Host the API's proxy adds, is sent on to the app's address (set by the AppHost).
 */
function sendBrowsersToTheApp(appUrl: string | undefined): Plugin {
  return {
    name: 'send-browsers-to-the-app',
    configureServer(server) {
      if (!appUrl) return
      server.middlewares.use((req, res, next) => {
        if (req.headers['x-forwarded-host'] || !req.headers.accept?.includes('text/html')) return next()
        res.statusCode = 307
        // Appended, not resolved, so a path like //example.com cannot send the browser to another host.
        res.setHeader('Location', `${appUrl}${req.url ?? '/'}`)
        res.end()
      })
    },
  }
}

// In development the API proxies Vite (YARP), so the browser only ever talks to the API origin. The AppHost starts this
// server with the port in PORT (AddViteApp); the built files land in dist/, which the API serves in static mode.
export default defineConfig({
  plugins: [vue(), tailwindcss(), sendBrowsersToTheApp(process.env.TEMPLATE_APP_URL)],
  // Every script and stylesheet tag gets nonce="__CSP_NONCE__", plus <meta property="csp-nonce">. The API replaces the placeholder
  // with a fresh nonce per response and sends it in the CSP header (api/BccCode.Template.Api/Frontend/IndexHtml.cs).
  html: { cspNonce: '__CSP_NONCE__' },
  resolve: { alias: { '@': fileURLToPath(new URL('./src', import.meta.url)) } },
  server: { port: Number(process.env.PORT ?? 16001), strictPort: true },
  test: { projects: [{ extends: true, test: { name: 'unit', environment: 'jsdom', include: ['src/**/*.test.ts'] } }] },
})
