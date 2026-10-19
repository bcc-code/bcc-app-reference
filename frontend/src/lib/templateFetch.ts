const UNSAFE_METHODS = new Set(['POST', 'PUT', 'PATCH', 'DELETE'])
export const XSRF_COOKIE = 'XSRF-TOKEN'
const XSRF_HEADER = 'X-XSRF-TOKEN'
export const TOKEN_ENDPOINT = '/user/antiforgery-token'

export function readCookie(name: string, cookieString: string): string | undefined {
  for (const part of cookieString.split(';')) {
    const [key, ...rest] = part.trim().split('=')
    if (key === name) return decodeURIComponent(rest.join('='))
  }
  return undefined
}

export interface TemplateFetchDeps {
  fetch: typeof fetch
  cookies: () => string
  redirect: (url: string) => void
  location: () => { pathname: string; search: string }
}

/**
 * The one fetch the generated client uses (standard 21 rule 7): double-submit CSRF token on unsafe methods (fetched on
 * demand), one retry when the server asks for a new token (X-Csrf-Refresh), and a 401 sends the browser to login with the
 * current page as returnUrl. Concurrent token refreshes share one request.
 */
export function createTemplateFetch(deps: TemplateFetchDeps): typeof fetch {
  let refreshInFlight: Promise<void> | undefined

  function refreshToken(): Promise<void> {
    refreshInFlight ??= deps
      .fetch(TOKEN_ENDPOINT, { credentials: 'same-origin' })
      .then(() => undefined)
      .finally(() => {
        refreshInFlight = undefined
      })
    return refreshInFlight
  }

  async function withXsrf(request: Request): Promise<Request> {
    if (!UNSAFE_METHODS.has(request.method)) return request
    let token = readCookie(XSRF_COOKIE, deps.cookies())
    if (!token) {
      await refreshToken()
      token = readCookie(XSRF_COOKIE, deps.cookies())
    }
    const headers = new Headers(request.headers)
    if (token) headers.set(XSRF_HEADER, token)
    return new Request(request, { headers })
  }

  return async (input, init) => {
    const original = new Request(input, init)
    let response = await deps.fetch(await withXsrf(original.clone()))
    if (response.status === 400 && response.headers.get('X-Csrf-Refresh') === '1') {
      await refreshToken()
      response = await deps.fetch(await withXsrf(original.clone()))
    }
    if (response.status === 401) {
      const { pathname, search } = deps.location()
      deps.redirect(`/user/login?returnUrl=${encodeURIComponent(pathname + search)}`)
    }
    return response
  }
}

export const templateFetch = createTemplateFetch({
  fetch: (input, init) => fetch(input, init),
  cookies: () => document.cookie,
  redirect: (url) => window.location.assign(url),
  location: () => window.location,
})
