import type { CreateClientConfig } from '@/client/client.gen'
import { createApiFetch } from '@/lib/apiFetch'

export const apiFetch = createApiFetch({
  fetch: (input, init) => fetch(input, init),
  navigate: (url) => window.location.assign(url),
  location: () => window.location,
})

/** Runtime config of the generated client (runtimeConfigPath): every generated call goes through the API fetch from the start. */
export const createClientConfig: CreateClientConfig = (config) => ({ ...config, fetch: apiFetch })
