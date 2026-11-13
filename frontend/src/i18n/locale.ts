import { supportedLanguages, type Language } from '.'

const STORAGE_KEY = 'language'

function isLanguage(value: string | null | undefined): value is Language {
  return supportedLanguages.some((language) => language === value)
}

// Reading globalThis.localStorage can itself throw when storage is blocked (private windows), so it happens inside the try.

/** The language the person picked last, if any. Blocked storage is the same as no choice. */
export function rememberedLanguage(storage?: Pick<Storage, 'getItem'>): Language | undefined {
  try {
    const stored = (storage ?? globalThis.localStorage).getItem(STORAGE_KEY)
    return isLanguage(stored) ? stored : undefined
  } catch {
    return undefined
  }
}

export function rememberLanguage(language: Language, storage?: Pick<Storage, 'setItem'>): void {
  try {
    ;(storage ?? globalThis.localStorage).setItem(STORAGE_KEY, language)
  } catch {
    // Only a convenience: the next visit starts from the browser's language again.
  }
  document.documentElement.lang = language
}
