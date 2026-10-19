// @vitest-environment node
import { describe, expect, it, vi } from 'vitest'
import { createTemplateFetch, readCookie } from './templateFetch'

const stale = () => new Response(null, { status: 400, headers: { 'X-Csrf-Refresh': '1' } })

function harness(responses: Response[], initialCookie = '') {
  let cookie = initialCookie
  const calls: Request[] = []
  let tokenRequests = 0
  const fetchFn = vi.fn<typeof fetch>(async (input, init) => {
    if (typeof input === 'string' && input.endsWith('/user/antiforgery-token')) {
      tokenRequests++
      await Promise.resolve()
      cookie = 'XSRF-TOKEN=fresh'
      return new Response(null, { status: 204 })
    }
    calls.push(new Request(input, init))
    return responses.shift() ?? new Response(null, { status: 200 })
  })
  const redirect = vi.fn<(url: string) => void>()
  const templateFetch = createTemplateFetch({
    fetch: fetchFn,
    cookies: () => cookie,
    redirect,
    location: () => ({ pathname: '/notes/1', search: '?x=2' }),
  })
  return { templateFetch, calls, redirect, tokenRequests: () => tokenRequests }
}

describe('readCookie', () => {
  it('finds a cookie among others and decodes it', () => {
    expect(readCookie('XSRF-TOKEN', 'a=1; XSRF-TOKEN=ab%3Dc; b=2')).toBe('ab=c')
    expect(readCookie('XSRF-TOKEN', 'a=1')).toBeUndefined()
  })
})

describe('templateFetch', () => {
  it('leaves safe methods alone', async () => {
    const { templateFetch, calls } = harness([], 'XSRF-TOKEN=t')
    await templateFetch('http://app.test/api/notes')
    expect(calls[0]?.headers.get('X-XSRF-TOKEN')).toBeNull()
  })

  it('echoes the XSRF cookie on unsafe methods', async () => {
    const { templateFetch, calls } = harness([], 'XSRF-TOKEN=t')
    await templateFetch('http://app.test/api/notes', { method: 'POST', body: '{}' })
    expect(calls[0]?.headers.get('X-XSRF-TOKEN')).toBe('t')
  })

  it('fetches a token first when the cookie is missing', async () => {
    const { templateFetch, calls } = harness([])
    await templateFetch('http://app.test/api/notes', { method: 'POST', body: '{}' })
    expect(calls[0]?.headers.get('X-XSRF-TOKEN')).toBe('fresh')
  })

  it('shares one token request between concurrent calls', async () => {
    const { templateFetch, tokenRequests } = harness([])
    await Promise.all([
      templateFetch('http://app.test/api/notes', { method: 'POST', body: '{}' }),
      templateFetch('http://app.test/api/notes', { method: 'POST', body: '{}' }),
    ])
    expect(tokenRequests()).toBe(1)
  })

  it('refreshes the token and retries once, with the same body, when the server asks for it', async () => {
    const { templateFetch, calls } = harness([stale(), new Response(null, { status: 200 })], 'XSRF-TOKEN=old')
    const response = await templateFetch('http://app.test/api/notes', { method: 'POST', body: '{"a":1}' })
    expect(response.status).toBe(200)
    expect(calls.map((c) => c.headers.get('X-XSRF-TOKEN'))).toEqual(['old', 'fresh'])
    expect(await calls[1]?.text()).toBe('{"a":1}')
  })

  it('retries only once', async () => {
    const { templateFetch, calls } = harness([stale(), stale()], 'XSRF-TOKEN=old')
    const response = await templateFetch('http://app.test/api/notes', { method: 'POST', body: '{}' })
    expect(response.status).toBe(400)
    expect(calls).toHaveLength(2)
  })

  it('does not retry a 400 without the refresh header', async () => {
    const { templateFetch, calls } = harness([new Response(null, { status: 400 })], 'XSRF-TOKEN=t')
    await templateFetch('http://app.test/api/notes', { method: 'POST', body: '{}' })
    expect(calls).toHaveLength(1)
  })

  it('sends an expired session to login and comes back to the same place', async () => {
    const { templateFetch, redirect } = harness([new Response(null, { status: 401 })], 'XSRF-TOKEN=t')
    await templateFetch('http://app.test/api/me')
    expect(redirect).toHaveBeenCalledWith('/user/login?returnUrl=%2Fnotes%2F1%3Fx%3D2')
  })
})
