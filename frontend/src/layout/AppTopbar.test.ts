import { QueryClient, VueQueryPlugin } from '@tanstack/vue-query'
import { createApp, nextTick } from 'vue'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { createMemoryHistory, createRouter } from 'vue-router'
import { apiFetch } from '@/api'
import { client } from '@/client/client.gen'
import { installBccComponentLibrary } from '@/csp'
import { createTemplateI18n } from '@/i18n'
import AppTopbar from './AppTopbar.vue'

// jsdom has no matchMedia, which PrimeVue's Select (the language switcher) listens to for the screen's orientation.
vi.stubGlobal('matchMedia', (media: string) => ({ media, matches: false, addEventListener() {}, removeEventListener() {} }))

/** The menu's links, in order, once /api/me has answered with the roles. */
async function linksFor(roles: string[]): Promise<{ links: string[]; signOut: boolean }> {
  client.setConfig({
    baseUrl: 'http://localhost',
    throwOnError: true,
    fetch: () => Promise.resolve(Response.json({ displayName: 'Test User', roles })),
  })
  const router = createRouter({
    history: createMemoryHistory(),
    routes: [{ path: '/:path(.*)*', component: { render: () => null } }],
  })
  await router.push('/')

  const element = document.createElement('div')
  const app = createApp(AppTopbar)
  installBccComponentLibrary(app, undefined)
  app.use(createTemplateI18n('en')).use(router).use(VueQueryPlugin, { queryClient: new QueryClient() })
  app.mount(element)
  await vi.waitFor(() => expect(element.querySelector('[data-testid=current-user-name]')?.textContent).toBe('Test User'))
  await nextTick()

  const links = [...element.querySelectorAll('nav a')].map((a) => a.textContent?.trim() ?? '')
  const signOut = element.querySelector('form[action="/auth/logout"][method=post] button[type=submit]') !== null
  app.unmount()
  return { links, signOut }
}

// The links are cosmetic: the router's guard (router.test.ts) and the API decide who may open a page.
describe('AppTopbar', () => {
  afterEach(() => client.setConfig({ fetch: apiFetch }))

  it('shows a member the notes and a sign-out button', async () => {
    expect(await linksFor([])).toEqual({ links: ['Notes'], signOut: true })
  })

  it('shows an administrator the statistics too', async () => {
    expect((await linksFor(['admin'])).links).toEqual(['Notes', 'Statistics'])
  })
})
