import { QueryClient } from '@tanstack/vue-query'
import { createMemoryHistory } from 'vue-router'
import { describe, expect, it } from 'vitest'
import { meQueryOptions } from '@/features/session/me'
import { createAppRouter } from './router'

function routerFor(roles: string[]) {
  const queryClient = new QueryClient()
  queryClient.setQueryData(meQueryOptions().queryKey, { displayName: 'Test User', roles })
  return createAppRouter(queryClient, createMemoryHistory())
}

describe('router', () => {
  it('sends the root to the notes', async () => {
    const router = routerFor([])
    await router.push('/')
    expect(router.currentRoute.value.path).toBe('/notes')
  })

  it('lets an admin open the statistics', async () => {
    const router = routerFor(['admin'])
    await router.push('/admin/stats')
    expect(router.currentRoute.value.path).toBe('/admin/stats')
  })

  it('shows the no-access page to a user without the admin role', async () => {
    const router = routerFor([])
    await router.push('/admin/stats')
    expect(router.currentRoute.value.path).toBe('/no-access')
  })

  it('does not ask for a role on the notes pages', async () => {
    const router = routerFor([])
    await router.push('/notes/new')
    expect(router.currentRoute.value.path).toBe('/notes/new')
  })
})
