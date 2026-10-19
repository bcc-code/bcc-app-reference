import { createI18n } from 'vue-i18n'
import en from './en.json'
import nb from './nb.json'

export const supportedLanguages = ['en', 'nb'] as const
export type Language = (typeof supportedLanguages)[number]
const fallback: Language = 'en'

/** The first browser language we have a translation for. Norwegian Bokmål, Nynorsk and the generic "no" all use nb. */
export function pickLanguage(candidates: readonly string[]): Language {
  for (const candidate of candidates) {
    const primary = candidate.toLowerCase().split('-')[0]
    if (primary === 'nb' || primary === 'nn' || primary === 'no') return 'nb'
    if (primary === 'en') return 'en'
  }
  return fallback
}

export function createTemplateI18n(locale: Language) {
  return createI18n({
    legacy: false,
    locale,
    fallbackLocale: fallback,
    messages: { en, nb },
    datetimeFormats: {
      en: { short: { dateStyle: 'medium', timeStyle: 'short' } },
      nb: { short: { dateStyle: 'medium', timeStyle: 'short' } },
    },
  })
}
