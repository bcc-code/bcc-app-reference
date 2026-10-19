import type { KnipConfig } from 'knip'

export default {
  project: ['src/**/*.{ts,vue,css}', 'e2e/**/*.ts'],
  ignore: ['src/client/**/*'],
} satisfies KnipConfig
