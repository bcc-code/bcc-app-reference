async page => {
  // Logs in through the OIDC mock as one persona, sets the 1280x720 viewport every capture script
  // assumes, and hides the Vue devtools button the dev server floats over every page.
  //   __PERSON__ = user   plain member
  //   __PERSON__ = user2  a second plain member, for ownership checks
  //   __PERSON__ = admin  has the admin role
  // Run once per session, right after: playwright-cli -s=<session> open --browser=chrome "$APP"
  //
  // The mock (api/OidcMock) shows a page with one button per persona (<button name="persona" value="user">).
  // Opening the app redirects there; a click signs in and the mock sends the browser back to the app.
  const PERSONS = ['user', 'user2', 'admin']
  const persona = '__PERSON__'
  if (!PERSONS.includes(persona)) throw new Error('replace __PERSON__ with user, user2 or admin')

  await page.setViewportSize({ width: 1280, height: 720 })
  await page.context().addInitScript(() => {
    document.addEventListener('DOMContentLoaded', () => {
      const style = document.createElement('style')
      style.textContent = '#__vue-devtools-container__ { display: none !important }'
      document.head.append(style)
    })
  })
  await page.waitForLoadState('networkidle')
  const button = page.locator(`button[name="persona"][value="${persona}"]`)
  if (await button.count()) {
    await button.click()
    // The mock's own page has no #app; the app has.
    await page.waitForSelector('#app > *', { timeout: 30000 })
  }
  await page.reload()
  await page.waitForLoadState('networkidle')
}
