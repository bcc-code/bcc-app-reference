import type { QueryClient } from '@tanstack/vue-query'
import { createRouter, createWebHistory, type RouterHistory } from 'vue-router'
import { mayOpenStats } from '@/admin/rules'
import type { GetMeResponse } from '@/client'
import { queryClient as sharedQueryClient } from '@/queryClient'
import { usersQueries } from '@/users/queries'

declare module 'vue-router' {
  interface RouteMeta {
    /** Served and shown without a session; nothing on the page may call an API that needs one before sign-in. */
    public?: boolean
    /**
     * Role-specific page (standard 21.16): who sees it rather than the no-access page. The server stays the authority; this
     * gives a clear page instead of a page full of 403s. The predicates are in each feature's rules.ts.
     */
    allowed?: (roles: readonly string[], me: GetMeResponse) => boolean
  }
}

export function createAppRouter(queryClient: QueryClient = sharedQueryClient, history: RouterHistory = createWebHistory()) {
  const router = createRouter({
    history,
    routes: [
      { path: '/', redirect: '/notes' },
      { path: '/notes', component: () => import('@/notes/NotesListPage.vue') },
      { path: '/notes/new', component: () => import('@/notes/NoteCreatePage.vue') },
      { path: '/notes/:noteId/edit', component: () => import('@/notes/NoteEditPage.vue'), props: true },
      { path: '/notes/:noteId/history', component: () => import('@/notes/NoteHistoryPage.vue'), props: true },
      { path: '/admin/stats', component: () => import('@/admin/StatsPage.vue'), meta: { allowed: mayOpenStats } },
      { path: '/no-access', name: 'no-access', component: () => import('@/components/NoAccessPage.vue') },
      { path: '/:pathMatch(.*)*', component: () => import('@/components/NotFoundPage.vue') },
    ],
  })

  router.beforeEach(async (to) => {
    const allowed = to.meta.allowed
    if (!allowed) return true
    const me = await queryClient.ensureQueryData(usersQueries.me())
    return allowed(me.roles, me) ? true : { name: 'no-access' }
  })

  return router
}
