import { defineConfig } from '@hey-api/openapi-ts'

// The contract is committed by the backend (api/openapi/openapi.json). CI regenerates the client and fails on a diff.
export default defineConfig({
  input: '../api/openapi/openapi.json',
  output: {
    path: './src/client',
    postProcess: [{ command: 'oxfmt', args: ['{{path}}'] }],
  },
  plugins: [
    { name: '@hey-api/sdk' },
    { name: '@hey-api/client-fetch', baseUrl: '', throwOnError: true },
    { name: '@hey-api/typescript', enums: { case: 'PascalCase', enabled: true, mode: 'javascript' } },
  ],
})
