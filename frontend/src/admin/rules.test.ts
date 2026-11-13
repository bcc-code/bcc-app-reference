import { describe, expect, it } from 'vitest'
import { ADMIN_ROLE, mayOpenStats } from './rules'

describe('admin rules', () => {
  it('opens the statistics for an administrator only', () => {
    expect(mayOpenStats([ADMIN_ROLE])).toBe(true)
    expect(mayOpenStats([])).toBe(false)
    expect(mayOpenStats(['member'])).toBe(false)
  })
})
