import { QueryClient } from '@tanstack/vue-query'
import { afterEach, describe, expect, it } from 'vitest'
import { apiFetch } from '@/api'
import { client } from '@/client/client.gen'
import { usersQueries } from './queries'

describe('usersQueries', () => {
  afterEach(() => client.setConfig({ fetch: apiFetch }))

  it('uses the users/me key', () => {
    expect(usersQueries.me().queryKey).toEqual(['users', 'me'])
  })

  it('fetches /api/me through the generated client', async () => {
    const requested: string[] = []
    client.setConfig({
      baseUrl: 'http://localhost',
      throwOnError: true,
      fetch: (input: RequestInfo | URL) => {
        requested.push(new URL(new Request(input).url).pathname)
        return Promise.resolve(Response.json({ displayName: 'Test User', roles: [] }))
      },
    })

    const result = await new QueryClient().fetchQuery(usersQueries.me())

    expect(requested).toEqual(['/api/me'])
    expect(result).toEqual({ displayName: 'Test User', roles: [] })
  })
})
