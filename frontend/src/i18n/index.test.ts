import { describe, expect, it } from 'vitest'
import { pickLanguage } from '.'

describe('pickLanguage', () => {
  it('takes the first language that has a translation', () => {
    expect(pickLanguage(['de-DE', 'nb-NO', 'en'])).toBe('nb')
  })

  it('maps Nynorsk and generic Norwegian to Bokmål', () => {
    expect(pickLanguage(['nn-NO'])).toBe('nb')
    expect(pickLanguage(['no'])).toBe('nb')
  })

  it('falls back to English', () => {
    expect(pickLanguage([])).toBe('en')
    expect(pickLanguage(['fr-FR'])).toBe('en')
  })
})
