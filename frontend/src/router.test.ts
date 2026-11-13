import { QueryClient } from '@tanstack/vue-query'
import { createMemoryHistory } from 'vue-router'
import { describe, expect, it, vi } from 'vitest'
import { usersQueries } from '@/users/queries'
import { createAppRouter } from './router'

// The router loads the page it lets through, and a page with the component library takes seconds to load in jsdom. The
// guard decides before that, so an empty page stands in.
vi.mock('@/notes/NoteCreatePage.vue', () => ({ default: { render: () => null } }))
vi.mock('@/notes/NoteHistoryPage.vue', () => ({ default: { render: () => null } }))

function routerFor(roles: string[]) {
  const queryClient = new QueryClient()
  queryClient.setQueryData(usersQueries.me().queryKey, { displayName: 'Test User', roles })
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
    await router.push('/notes/n-1/history')
    expect(router.currentRoute.value.path).toBe('/notes/n-1/history')
  })
})
