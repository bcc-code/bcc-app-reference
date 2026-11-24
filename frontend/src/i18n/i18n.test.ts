// @vitest-environment node
import { readdirSync, readFileSync } from 'node:fs'
import { join } from 'node:path'
import { describe, expect, it } from 'vitest'
import { createTemplateI18n, supportedLanguages } from '.'

type Tree = { [key: string]: string | Tree }

/** `notes.form.title` -> the message, for every leaf. */
function flatten(tree: Tree, prefix = ''): Map<string, string> {
  const result = new Map<string, string>()
  for (const [key, value] of Object.entries(tree)) {
    const path = prefix + key
    if (typeof value === 'string') result.set(path, value)
    else for (const [inner, text] of flatten(value, path + '.')) result.set(inner, text)
  }
  return result
}

const i18nDir = import.meta.dirname
const localeFiles = readdirSync(i18nDir).filter((name) => name.endsWith('.json'))
const messages: Record<string, Map<string, string>> = Object.fromEntries(
  localeFiles.map((name) => [
    name.replace(/\.json$/, ''),
    flatten(JSON.parse(readFileSync(join(i18nDir, name), 'utf8')) as Tree),
  ]),
)
const english = messages.en!
const others = Object.keys(messages).filter((language) => language !== 'en')

function files(dir: string, extensions: string[], skip: (path: string) => boolean): string[] {
  return readdirSync(dir, { withFileTypes: true }).flatMap((entry) => {
    const path = join(dir, entry.name)
    if (skip(path)) return []
    if (entry.isDirectory()) return files(path, extensions, skip)
    return extensions.some((extension) => entry.name.endsWith(extension)) ? [path] : []
  })
}

const posix = (path: string) => path.replaceAll('\\', '/')
const read = (paths: string[]) => paths.map((path) => readFileSync(path, 'utf8')).join('\n')

const frontendSource = read(
  files(join(i18nDir, '..'), ['.vue', '.ts'], (path) => /\/client(\/|$)/.test(posix(path)) || path.endsWith('.test.ts')),
)

// The API names the `errors.*` keys it sends in ProblemDetails (`key` and field errors) as string constants, so a key that
// only the backend names is in use. The API's tests are left out: a test naming a key does not make it a feature.
const backendSource = read(
  files(join(i18nDir, '..', '..', '..', 'api'), ['.cs'], (path) => /\/(bin|obj|node_modules|tests)(\/|$)/.test(posix(path))),
)

/** Keys built at run time, as `t(\`prefix.${value}\`)`. Each pattern names the place that builds them and must match a key. */
const runtimeKeys: { pattern: RegExp; builtBy: string }[] = [
  // The failed Vuelidate rule's name.
  { pattern: /^form\.alerts\.[A-Za-z0-9]+$/, builtBy: 'errorMessages in components/validation.ts' },
]

function isUsed(key: string): boolean {
  const escaped = key.replaceAll('.', String.raw`\.`)
  const quoted = new RegExp(`['"\`]${escaped}['"\`]`)
  return quoted.test(frontendSource) || quoted.test(backendSource) || runtimeKeys.some(({ pattern }) => pattern.test(key))
}

function placeholders(text: string): string[] {
  return [...new Set([...text.matchAll(/\{(\w+)\}/g)].map((match) => match[1]!))].toSorted()
}

describe('translations', () => {
  it('have a file for every supported language and no other', () => {
    expect(Object.keys(messages).toSorted()).toEqual([...supportedLanguages].toSorted())
  })

  it.each(others)('have the same keys in en.json and %s.json', (language) => {
    const map = messages[language]!
    expect({
      [`missing in ${language}.json`]: [...english.keys()].filter((key) => !map.has(key)),
      'missing in en.json': [...map.keys()].filter((key) => !english.has(key)),
    }).toEqual({ [`missing in ${language}.json`]: [], 'missing in en.json': [] })
  })

  it.each(others)('have the same placeholders in en.json and %s.json', (language) => {
    const map = messages[language]!
    const different = [...english]
      .filter(([key, text]) => map.has(key) && placeholders(text).join() !== placeholders(map.get(key)!).join())
      .map(([key]) => key)
    expect(different).toEqual([])
  })

  it('have no empty text', () => {
    const empty = Object.entries(messages).flatMap(([language, map]) =>
      [...map].filter(([, text]) => text.trim() === '').map(([key]) => `${language}: ${key}`),
    )
    expect(empty).toEqual([])
  })

  // A key listed here is in en.json but nothing names it: remove it from every language file, or add its run-time pattern to runtimeKeys.
  it('have no key that nothing uses', () => {
    expect([...english.keys()].filter((key) => !isUsed(key))).toEqual([])
  })

  // vue-i18n compiles a message the first time it is shown, so a message it cannot parse only fails on the page that shows
  // it: a literal `@` in a hint (a linked message to vue-i18n) is one example. A literal `@`, `{`, `}` or `|` goes in as {'@'} and so on.
  it('compile every message', () => {
    const failures = Object.entries(messages).flatMap(([language, map]) => {
      const { t } = createTemplateI18n(language as (typeof supportedLanguages)[number]).global
      return [...map.keys()].flatMap((key) => {
        try {
          t(key)
          return []
        } catch (error) {
          return [`${language}: ${key}: ${(error as Error).message.split('\n')[0]}`]
        }
      })
    })
    expect(failures).toEqual([])
  })

  // A pattern that outlives the code that builds its keys would hide an unused key.
  it('have a key for every runtime pattern', () => {
    const dead = runtimeKeys
      .filter(({ pattern }) => ![...english.keys()].some((key) => pattern.test(key)))
      .map(({ builtBy }) => builtBy)
    expect(dead).toEqual([])
  })

  it('have every errors key the API names', () => {
    const named = [...backendSource.matchAll(/"(errors(?:\.[A-Za-z]+)+)"/g)].map((match) => match[1]!)
    expect(named.filter((key) => !english.has(key))).toEqual([])
  })
})
