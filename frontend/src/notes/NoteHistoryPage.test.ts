import { QueryClient, VueQueryPlugin } from '@tanstack/vue-query'
import { createApp } from 'vue'
import { afterEach, describe, expect, it } from 'vitest'
import { createMemoryHistory, createRouter } from 'vue-router'
import { apiFetch } from '@/api'
import { client } from '@/client/client.gen'
import { createTemplateI18n } from '@/i18n'
import NoteHistoryPage from './NoteHistoryPage.vue'

describe('NoteHistoryPage', () => {
  afterEach(() => client.setConfig({ fetch: apiFetch }))

  it('lists the steps of the note with their names, and shows a step it does not know by its name', async () => {
    const requested: string[] = []
    client.setConfig({
      baseUrl: 'http://localhost',
      throwOnError: true,
      fetch: (input: RequestInfo | URL) => {
        requested.push(new URL(new Request(input).url).pathname)
        return Promise.resolve(
          Response.json({
            id: 'n-1',
            entries: [
              { at: '2026-01-02T10:00:00Z', step: 'NoteCreated', version: 1, title: 'First', comment: null },
              { at: '2026-01-03T10:00:00Z', step: 'NoteEdited', version: 2, title: 'Second', comment: 'Fixed a typo' },
              { at: '2026-01-04T10:00:00Z', step: 'NoteShared', version: 3, title: 'Second', comment: null },
            ],
          }),
        )
      },
    })
    const router = createRouter({
      history: createMemoryHistory(),
      routes: [{ path: '/:path(.*)*', component: { render: () => null } }],
    })
    const element = document.createElement('div')
    const app = createApp(NoteHistoryPage, { noteId: 'n-1' })
    app.use(createTemplateI18n('en')).use(router).use(VueQueryPlugin, { queryClient: new QueryClient() })
    app.mount(element)

    await expect.poll(() => element.querySelectorAll('li').length).toBe(3)
    expect(requested).toEqual(['/api/notes/n-1/history'])
    expect([...element.querySelectorAll('li')].map((li) => li.querySelector('p')?.textContent)).toEqual([
      'Created',
      'Edited',
      'NoteShared',
    ])
    expect(element.textContent).toContain('Fixed a typo')
    app.unmount()
  })
})
