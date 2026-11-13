import { defineConfig } from '@hey-api/openapi-ts'

// The contract is committed by the backend (api/BccCode.Template.Api/BccCode.Template.Api.json, written by `pnpm generate`). CI regenerates the client and fails on a diff.
export default defineConfig({
  input: '../api/BccCode.Template.Api/BccCode.Template.Api.json',
  output: {
    path: './src/client',
    postProcess: [
      {
        command: 'oxfmt',
        args: ['{{path}}'],
      },
    ],
  },
  plugins: [
    {
      name: '@hey-api/sdk',
      operations: {
        methodName: {
          casing: 'camelCase',
        },
        strategy: 'byTags',
        nesting(operation) {
          return [operation.operationId ?? operation.id]
        },
        containerName: (n) => n + 'Client',
      },
    },
    {
      name: '@hey-api/client-fetch',
      baseUrl: '',
      throwOnError: true,
      runtimeConfigPath: './src/api.ts',
    },
    {
      name: '@hey-api/typescript',
      enums: {
        case: 'PascalCase',
        enabled: true,
        mode: 'javascript',
      },
    },
  ],
})
