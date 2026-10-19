import { defineConfig } from '@playwright/test'

// Runs against a live app: `aspire run` (with the Vite resource) or the E2E=true topology that serves frontend/dist.
// The address comes from the environment; `aspire describe` shows the API's https URL.
const baseURL = process.env.E2E_BASE_URL ?? 'https://localhost:18000'

export default defineConfig({
  testDir: './e2e',
  fullyParallel: false,
  workers: 1,
  retries: process.env.CI ? 1 : 0,
  reporter: process.env.CI ? [['github'], ['html', { open: 'never' }]] : 'list',
  use: {
    baseURL,
    ignoreHTTPSErrors: true,
    locale: 'en-US',
    timezoneId: 'Europe/Oslo',
    trace: 'retain-on-failure',
  },
})
