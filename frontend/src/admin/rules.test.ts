import { describe, expect, it } from 'vitest'
import { ADMIN_ROLE, mayOpenStats, providerLabelKey } from './rules'

describe('admin rules', () => {
  it('opens the statistics for an administrator only', () => {
    expect(mayOpenStats([ADMIN_ROLE])).toBe(true)
    expect(mayOpenStats([])).toBe(false)
    expect(mayOpenStats(['member'])).toBe(false)
  })

  it('labels the providers the API names and any other as other', () => {
    expect(providerLabelKey('bcc-signon')).toBe('admin.people.provider.bccSignon')
    expect(providerLabelKey('entra')).toBe('admin.people.provider.entra')
    expect(providerLabelKey('something-new')).toBe('admin.people.provider.other')
  })
})
