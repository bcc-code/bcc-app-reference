// @vitest-environment node
import { describe, expect, it, vi } from 'vitest'
import { logoutToken } from './logout'

describe('logoutToken', () => {
  it('uses the XSRF cookie when there is one', async () => {
    const fetchFn = vi.fn<typeof fetch>()
    expect(await logoutToken({ fetch: fetchFn, cookies: () => 'a=1; XSRF-TOKEN=t' })).toBe('t')
    expect(fetchFn).not.toHaveBeenCalled()
  })

  it('fetches a token first when the cookie is missing', async () => {
    let cookie = ''
    const fetchFn = vi.fn<typeof fetch>(async () => {
      cookie = 'XSRF-TOKEN=fresh'
      return new Response(null, { status: 204 })
    })
    expect(await logoutToken({ fetch: fetchFn, cookies: () => cookie })).toBe('fresh')
    expect(fetchFn).toHaveBeenCalledWith('/user/antiforgery-token', { credentials: 'same-origin' })
  })
})
