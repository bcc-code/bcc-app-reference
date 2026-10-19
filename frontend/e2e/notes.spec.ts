import { expect, test } from '@playwright/test'
import { expectAccessible, signIn, uniqueTitle } from './support'

test.describe('notes', () => {
  test('a user creates, edits and archives a note', async ({ page }) => {
    const title = uniqueTitle('Plan')
    const edited = `${title} (edited)`

    await signIn(page, 'user')
    await expect(page.getByRole('heading', { name: 'Notes', level: 1 })).toBeVisible()
    await expectAccessible(page)

    await page.getByRole('link', { name: 'New note' }).click()
    await expect(page.getByRole('heading', { name: 'New note' })).toBeVisible()
    await expectAccessible(page)
    await page.getByLabel('Title').fill(title)
    await page.getByLabel('Text').fill('First line\nSecond line')
    await page.getByRole('button', { name: 'Create note' }).click()
    await expect(page.getByRole('link', { name: `Edit ${title}` })).toBeVisible()
    await expectAccessible(page)

    await page.getByRole('link', { name: `Edit ${title}` }).click()
    await expect(page.getByRole('heading', { name: 'Edit note' })).toBeVisible()
    await expect(page.getByLabel('Title')).toHaveValue(title)
    await expectAccessible(page)
    await page.getByLabel('Title').fill(edited)
    await page.getByRole('button', { name: 'Save changes' }).click()
    await expect(page.getByRole('link', { name: `Edit ${edited}` })).toBeVisible()

    await page.getByRole('button', { name: `Archive ${edited}` }).click()
    await expect(page.getByRole('link', { name: `Edit ${edited}` })).toHaveCount(0)
    await expectAccessible(page)
  })

  test('saving a note that changed in the meantime shows the conflict and offers the latest version', async ({
    page,
    context,
  }) => {
    const title = uniqueTitle('Shared')
    await signIn(page, 'user')
    await page.getByRole('link', { name: 'New note' }).click()
    await page.getByLabel('Title').fill(title)
    await page.getByRole('button', { name: 'Create note' }).click()
    await page.getByRole('link', { name: `Edit ${title}` }).click()
    await expect(page.getByLabel('Title')).toHaveValue(title)
    const editUrl = page.url()

    // A second tab saves first, so the first tab holds a stale version.
    const other = await context.newPage()
    await other.goto(editUrl)
    await other.getByLabel('Title').fill(`${title} from the other tab`)
    await other.getByRole('button', { name: 'Save changes' }).click()
    await expect(other.getByRole('link', { name: `Edit ${title} from the other tab` })).toBeVisible()
    await other.close()

    await page.getByLabel('Title').fill(`${title} from this tab`)
    await page.getByRole('button', { name: 'Save changes' }).click()
    await expect(page.getByRole('alert')).toContainText('The note was changed after you opened it')
    await expectAccessible(page)

    await page.getByRole('button', { name: 'Load the latest version' }).click()
    await expect(page.getByLabel('Title')).toHaveValue(`${title} from the other tab`)
    await expect(page.getByRole('alert')).toHaveCount(0)

    await page.goto('/notes')
    await page.getByRole('button', { name: `Archive ${title} from the other tab` }).click()
  })

  test('another user does not see the note', async ({ browser }) => {
    const title = uniqueTitle('Private')
    const owner = await browser.newContext()
    const ownerPage = await owner.newPage()
    await signIn(ownerPage, 'user')
    await ownerPage.getByRole('link', { name: 'New note' }).click()
    await ownerPage.getByLabel('Title').fill(title)
    await ownerPage.getByRole('button', { name: 'Create note' }).click()
    await expect(ownerPage.getByRole('link', { name: `Edit ${title}` })).toBeVisible()
    const editUrl = await ownerPage.getByRole('link', { name: `Edit ${title}` }).getAttribute('href')

    const other = await browser.newContext()
    const otherPage = await other.newPage()
    await signIn(otherPage, 'user2')
    await expect(otherPage.getByRole('link', { name: `Edit ${title}` })).toHaveCount(0)
    await otherPage.goto(editUrl!)
    await expect(otherPage.getByRole('alert')).toContainText('We could not find what you asked for.')

    await ownerPage.getByRole('button', { name: `Archive ${title}` }).click()
    await owner.close()
    await other.close()
  })
})
