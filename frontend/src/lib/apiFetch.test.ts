import { describe, expect, it } from 'vitest'
import { createApiFetch } from './apiFetch'

function setup(options: { respond?: () => Response; page?: string } = {}) {
  const state = { navigated: [] as string[], calls: [] as Request[] }
  const page = new URL(options.page ?? 'https://app.test/notes?a=b#frag')
  const apiFetch = createApiFetch({
    fetch: async (input, init) => {
      state.calls.push(new Request(input, init))
      return options.respond?.() ?? new Response('ok')
    },
    navigate: (url) => state.navigated.push(url),
    location: () => ({ pathname: page.pathname, search: page.search, hash: page.hash }),
  })
  return { apiFetch, state }
}

describe('createApiFetch', () => {
  it('passes the request and the response through unchanged', async () => {
    const { apiFetch, state } = setup()
    const response = await apiFetch('https://app.test/api/x', { method: 'POST', body: 'payload' })
    expect(await response.text()).toBe('ok')
    expect(state.calls.map((c) => `${c.method} ${c.url}`)).toEqual(['POST https://app.test/api/x'])
    expect(await state.calls[0]!.text()).toBe('payload')
  })

  it('passes 403 through', async () => {
    const { apiFetch, state } = setup({ respond: () => new Response('no', { status: 403 }) })
    const response = await apiFetch('https://app.test/api/notes')
    expect(response.status).toBe(403)
    expect(state.navigated).toEqual([])
  })

  it('navigates to login with the encoded current location on 401 and never resolves', async () => {
    const { apiFetch, state } = setup({
      page: 'https://app.test/a b/c?x=1&y=2#h',
      respond: () => new Response(null, { status: 401 }),
    })
    const outcome = await Promise.race([
      apiFetch('https://app.test/api/notes').then(() => 'resolved'),
      new Promise((resolve) => setTimeout(() => resolve('pending'), 20)),
    ])
    expect(outcome).toBe('pending')
    expect(state.navigated).toEqual(['/auth/login?returnUrl=' + encodeURIComponent('/a%20b/c?x=1&y=2#h')])
    expect(state.navigated[0]).toBe('/auth/login?returnUrl=%2Fa%2520b%2Fc%3Fx%3D1%26y%3D2%23h')
  })
})
