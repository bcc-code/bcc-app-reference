/** ProblemDetails from the API carry i18n keys: `key` (set by the server), `type` (urn:bcc:template:<key>) or, for validation, `errors`. */
interface ProblemBody {
  key?: unknown
  type?: unknown
  status?: unknown
  errors?: unknown
}

const KEY = /^errors(\.[A-Za-z]+)+$/
const TYPE_PREFIX = 'urn:bcc:template:'

function isKey(value: unknown): value is string {
  return typeof value === 'string' && KEY.test(value)
}

function bodyOf(error: unknown): ProblemBody {
  return typeof error === 'object' && error !== null ? (error as ProblemBody) : {}
}

function fieldKeys(errors: unknown): string[] {
  if (typeof errors !== 'object' || errors === null) return []
  return Object.values(errors).flatMap((values: unknown) => (Array.isArray(values) ? values.filter(isKey) : []))
}

function typeKey(type: unknown): string | undefined {
  if (typeof type !== 'string' || !type.startsWith(TYPE_PREFIX)) return undefined
  const key = type.slice(TYPE_PREFIX.length)
  return isKey(key) ? key : undefined
}

/**
 * A 403 and a 404 have no body (standard 09.10), so the generated client throws an empty object and the status is lost. The
 * client's error interceptor (main.ts) puts the status of the response on whatever it threw, so the rest of the SPA can tell them apart.
 */
export function withStatus(error: unknown, response?: Response): unknown {
  if (!response) return error
  const body = bodyOf(error)
  return typeof body.status === 'number' ? error : { ...body, status: response.status }
}

/** The i18n keys to show for a failed request, each once. Never empty. */
export function problemKeys(error: unknown): string[] {
  const body = bodyOf(error)
  const fields = fieldKeys(body.errors)
  if (fields.length > 0) return [...new Set(fields)]
  if (isKey(body.key)) return [body.key]
  const fromType = typeKey(body.type)
  if (fromType) return [fromType]
  if (body.status === 409) return ['errors.conflict']
  if (body.status === 403) return ['errors.forbidden']
  if (body.status === 404) return ['errors.notFound']
  return ['errors.unexpected']
}

/** True for a 409, the answer to an edit or archive on a stale version. */
export function isConflict(error: unknown): boolean {
  return bodyOf(error).status === 409
}
