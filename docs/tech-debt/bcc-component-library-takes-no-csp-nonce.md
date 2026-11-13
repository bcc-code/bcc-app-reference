---
title: "The BCC component library takes no CSP nonce, so the app wraps app.use to pass it to PrimeVue"
status: blocked
blocked-on: "the BCC component library (bcc-code/bcc-design) accepting install options such as a CSP nonce"
effort: S
priority: P3
scope: frontend
area: hygiene
---

# The BCC component library takes no CSP nonce

`BccComponentLibrary` (`@bcc-code/component-library-vue`, `dist/setup.js`) installs PrimeVue with a fixed config and no
`csp` option. PrimeVue injects its common theme as `<style>` elements during that install, so setting
`$primevue.config.csp.nonce` afterwards is too late, and the shell CSP (`style-src 'self' 'nonce-…'`) blocks them.

`frontend/src/csp.ts` works around it: while the library installs, it wraps `app.use` and adds `csp: { nonce }` to the
options that contain a `theme` (the PrimeVue config). This depends on how the library installs PrimeVue internally;
`csp.test.ts` runs the real library and fails if a `<style>` element is missing the nonce.

Fix: ask the library ([bcc-code/bcc-design](https://github.com/bcc-code/bcc-design)) to accept options in its install, for
example `app.use(BccComponentLibrary, { csp: { nonce } })`, and pass them on to PrimeVue. Then replace
`installBccComponentLibrary` with that call and keep the test.
