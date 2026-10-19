import { readCookie, TOKEN_ENDPOINT, XSRF_COOKIE } from './templateFetch'

export const LOGOUT_URL = '/user/logout'
/** The form field the server's antiforgery validation reads on a form post. */
export const LOGOUT_TOKEN_FIELD = '__RequestVerificationToken'

export interface LogoutDeps {
  fetch: typeof fetch
  cookies: () => string
}

/**
 * The token to put in the logout form. Logout is a real form post (a fetch cannot follow the redirects through the identity
 * provider), so the token goes in a hidden field instead of a header. It is the XSRF-TOKEN cookie, fetched first when the
 * page has not made an unsafe call yet.
 */
export async function logoutToken(deps: LogoutDeps): Promise<string> {
  const existing = readCookie(XSRF_COOKIE, deps.cookies())
  if (existing) return existing
  await deps.fetch(TOKEN_ENDPOINT, { credentials: 'same-origin' })
  return readCookie(XSRF_COOKIE, deps.cookies()) ?? ''
}
