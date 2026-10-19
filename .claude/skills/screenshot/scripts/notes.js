async page => {
  // Notes page, as user. Example script: copy it as the starting point for a new area.
  //   notes-list          the signed-in user's note list (empty on a fresh database)
  //   notes-create        the create-note form, empty, opened and not submitted
  // Data: none; an empty list is a valid state.
  // Routes and labels are from frontend/src/router.ts and frontend/src/i18n/{en,nb}.json (New note / Nytt notat).
  const OUT = '__OUT__'
  const APP = page.url().match(/^https:\/\/[^/]+/)?.[0]
  if (!APP || (await page.title()).includes('OIDC mock')) throw new Error('run login.js first')

  const open = async path => {
    await page.goto(APP + path)
    await page.waitForLoadState('networkidle')
    await page.waitForTimeout(1000)
  }
  const shot = (state, fullPage = true) => page.screenshot({ path: `${OUT}/notes-${state}.png`, fullPage })

  await open('/notes')
  await shot('list')

  await page.getByRole('link', { name: /new note|nytt notat/i }).click()
  await page.getByRole('heading', { level: 1, name: /new note|nytt notat/i }).waitFor()
  await page.waitForTimeout(500)
  await shot('create', false)
}
