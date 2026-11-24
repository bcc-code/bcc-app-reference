import { describe, expect, it } from 'vitest'
import { isConflict, problemKeys, withStatus } from './problems'

describe('problemKeys', () => {
  it('uses the messages of a validation problem, once each', () => {
    expect(
      problemKeys({
        status: 400,
        errors: { title: ['errors.note.titleRequired'], body: ['errors.note.bodyTooLong', 'errors.note.bodyTooLong'] },
      }),
    ).toEqual(['errors.note.titleRequired', 'errors.note.bodyTooLong'])
  })

  it('uses the key extension of a domain problem', () => {
    expect(problemKeys({ status: 409, key: 'errors.note.staleVersion' })).toEqual(['errors.note.staleVersion'])
  })

  it('falls back to the type URN', () => {
    expect(problemKeys({ status: 404, type: 'urn:bcc:template:errors.notFound' })).toEqual(['errors.notFound'])
  })

  it('reports a conflict without a key as a concurrent change', () => {
    expect(problemKeys({ status: 409, type: 'https://tools.ietf.org/html/rfc9110#section-15.5.10' })).toEqual(['errors.conflict'])
  })

  it('maps a bodiless 403 and 404 by their status', () => {
    expect(problemKeys({ status: 403 })).toEqual(['errors.forbidden'])
    expect(problemKeys({ status: 404 })).toEqual(['errors.notFound'])
  })

  it('falls back to the unexpected error', () => {
    expect(problemKeys(new TypeError('Failed to fetch'))).toEqual(['errors.unexpected'])
    expect(problemKeys('boom')).toEqual(['errors.unexpected'])
    expect(problemKeys({ status: 400, errors: { title: ['Not a key'] } })).toEqual(['errors.unexpected'])
  })
})

describe('isConflict', () => {
  it('is true for status 409 only', () => {
    expect(isConflict({ status: 409 })).toBe(true)
    expect(isConflict({ status: 404 })).toBe(false)
    expect(isConflict(undefined)).toBe(false)
  })
})

describe('withStatus', () => {
  it('puts the status of the response on an error that has none', () => {
    expect(withStatus({}, new Response(null, { status: 403 }))).toEqual({ status: 403 })
    expect(withStatus('', new Response(null, { status: 404 }))).toEqual({ status: 404 })
  })

  it('keeps the body of a problem, and the status it already carries', () => {
    expect(withStatus({ key: 'errors.note.staleVersion', status: 409 }, new Response(null, { status: 409 }))).toEqual({
      key: 'errors.note.staleVersion',
      status: 409,
    })
    expect(withStatus({ key: 'errors.x' }, new Response(null, { status: 400 }))).toEqual({ key: 'errors.x', status: 400 })
  })

  it('leaves an error without a response, such as a network failure, alone', () => {
    const failure = new TypeError('Failed to fetch')
    expect(withStatus(failure, undefined)).toBe(failure)
  })
})
