const LOGIN_PATH = '/auth/login'

export interface ApiFetchDeps {
  fetch: typeof fetch
  /** Full page navigation. */
  navigate: (url: string) => void
  location: () => { pathname: string; search: string; hash: string }
}

/**
 * The one fetch for all API traffic (standard 21.7): a 401 sends the person to sign in with a full page load. 403 is
 * passed through for the caller to show. CSRF needs nothing here: the server refuses requests from other origins.
 */
export function createApiFetch(deps: ApiFetchDeps): typeof fetch {
  return async (input, init) => {
    const response = await deps.fetch(input, init)
    if (response.status !== 401) return response
    const { pathname, search, hash } = deps.location()
    deps.navigate(`${LOGIN_PATH}?returnUrl=${encodeURIComponent(pathname + search + hash)}`)
    // The page is going away; never resolve as success.
    return new Promise<never>(() => {})
  }
}
