import { expect, test } from '@playwright/test'
import { expectAccessible, signIn } from './support'

test.describe('admin', () => {
  test('an admin sees the statistics', async ({ page }) => {
    await signIn(page, 'admin', '/admin/stats')
    await expect(page.getByRole('heading', { name: 'Statistics', level: 1 })).toBeVisible()
    await expect(page.getByTestId('stats-active')).toBeVisible()
    await expect(page.getByTestId('stats-archived')).toBeVisible()
    await expectAccessible(page)
  })

  test('a plain user gets the no-access page and no link to the statistics', async ({ page }) => {
    await signIn(page, 'user', '/no-access')
    await page.goto('/admin/stats')
    await expect(page.getByRole('heading', { name: 'No access', level: 1 })).toBeVisible()
    await expect(page.getByRole('link', { name: 'Statistics' })).toHaveCount(0)
    await expectAccessible(page)
  })

  test('signing out ends the session', async ({ page }) => {
    await signIn(page, 'user')
    await page.getByTestId('user-menu').click()
    await page.getByRole('button', { name: 'Sign out' }).click()
    // The identity provider's page, or the app's own sign-in redirect, shows the personas again.
    await expect(page.locator('button[name="persona"]').first()).toBeVisible()
  })
})
