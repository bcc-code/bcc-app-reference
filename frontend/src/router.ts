import type { QueryClient } from '@tanstack/vue-query'
import { createRouter, createWebHistory, type RouterHistory } from 'vue-router'
import { ADMIN_ROLE, hasRole, meQueryOptions } from '@/features/session/me'

declare module 'vue-router' {
  interface RouteMeta {
    /** The role the page needs. The server stays the authority; this gives a clear page instead of a page full of 403s. */
    role?: string
  }
}

export function createAppRouter(queryClient: QueryClient, history: RouterHistory = createWebHistory()) {
  const router = createRouter({
    history,
    routes: [
      { path: '/', redirect: '/notes' },
      { path: '/notes', component: () => import('@/features/notes/NotesListPage.vue') },
      { path: '/notes/new', component: () => import('@/features/notes/NoteCreatePage.vue') },
      { path: '/notes/:noteId/edit', component: () => import('@/features/notes/NoteEditPage.vue'), props: true },
      { path: '/admin/stats', component: () => import('@/features/admin/StatsPage.vue'), meta: { role: ADMIN_ROLE } },
      { path: '/no-access', name: 'no-access', component: () => import('@/components/NoAccessPage.vue') },
      { path: '/:pathMatch(.*)*', component: () => import('@/components/NotFoundPage.vue') },
    ],
  })

  router.beforeEach(async (to) => {
    const role = to.meta.role
    if (!role) return true
    const me = await queryClient.ensureQueryData(meQueryOptions())
    return hasRole(me, role) ? true : { name: 'no-access' }
  })

  return router
}
