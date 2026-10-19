import AxeBuilder from '@axe-core/playwright'
import { expect, type Page } from '@playwright/test'

export type Persona = 'user' | 'user2' | 'admin'

/**
 * Opens the app and signs in through the OIDC mock (api/OidcMock): the app redirects to the mock's page, which has one
 * button per persona. Ends on the app's notes page.
 */
export async function signIn(page: Page, persona: Persona, path = '/notes') {
  await page.goto(path)
  await page.locator(`button[name="persona"][value="${persona}"]`).click()
  // The mock's own page also has an h1, so wait for the way back to the app first.
  await expect(page).toHaveURL(new RegExp(`${path}$`))
  await expect(page.getByRole('heading', { level: 1 })).toBeVisible()
}

/** The part of the browser's `document` used in page functions below. */
interface BrowserGlobals {
  document: { getAnimations(): { playState: string }[] }
}

/** WCAG 2.1 A and AA (standard 19). Run it once the page shows its content, in every flow. */
export async function expectAccessible(page: Page) {
  // Messages and dialogs fade in. Measured mid-fade, a colour is still partly transparent and fails the contrast rule.
  // A function, not a string: the app's CSP forbids eval. It runs in the browser, and this tsconfig (node) has no DOM types.
  await page.waitForFunction(() =>
    (globalThis as unknown as BrowserGlobals).document.getAnimations().every((a) => a.playState !== 'running'),
  )
  const results = await new AxeBuilder({ page }).withTags(['wcag2a', 'wcag2aa', 'wcag21a', 'wcag21aa']).analyze()
  expect(results.violations.map((v) => `${v.id}: ${v.help} (${v.nodes.map((n) => n.target.join(' ')).join(', ')})`)).toEqual([])
}

/** A title no earlier run has used, because the database outlives a test run. */
export function uniqueTitle(prefix: string) {
  return `${prefix} ${Date.now()}-${Math.floor(Math.random() * 1000)}`
}
