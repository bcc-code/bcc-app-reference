import { describe, expect, it } from 'vitest'
import { ADMIN_ROLE, hasRole } from './me'

describe('hasRole', () => {
  it('is true when the user has the role', () => {
    expect(hasRole({ roles: [ADMIN_ROLE] }, ADMIN_ROLE)).toBe(true)
  })

  it('is false without the role or before the user has loaded', () => {
    expect(hasRole({ roles: [] }, ADMIN_ROLE)).toBe(false)
    expect(hasRole(undefined, ADMIN_ROLE)).toBe(false)
  })
})
