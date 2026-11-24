import { describe, expect, it } from 'vitest'
import { rememberLanguage, rememberedLanguage } from './locale'

describe('language choice', () => {
  it('has no choice until the person picked a language, or when the stored value is unknown', () => {
    expect(rememberedLanguage({ getItem: () => null })).toBeUndefined()
    expect(rememberedLanguage({ getItem: () => 'nb' })).toBe('nb')
    expect(rememberedLanguage({ getItem: () => 'de' })).toBeUndefined()
  })

  it('has no choice when storage is blocked', () => {
    const blocked = {
      getItem: () => {
        throw new Error('SecurityError')
      },
    }
    expect(rememberedLanguage(blocked)).toBeUndefined()
  })

  it('remembers the choice and sets the page language', () => {
    const stored = new Map<string, string>()
    rememberLanguage('nb', { setItem: (key, value) => stored.set(key, value) })

    expect(stored.get('language')).toBe('nb')
    expect(document.documentElement.lang).toBe('nb')
  })

  it('still sets the page language when storage is blocked', () => {
    rememberLanguage('en', {
      setItem: () => {
        throw new Error('SecurityError')
      },
    })

    expect(document.documentElement.lang).toBe('en')
  })
})
