import { BccComponentLibrary } from '@bcc-code/component-library-vue'
import type { App } from 'vue'

/** The nonce the API puts on the page and in its CSP header (html.cspNonce in vite.config.ts). */
export function cspNonce(): string | undefined {
  return document.querySelector<HTMLMetaElement>('meta[property="csp-nonce"]')?.nonce || undefined
}

/**
 * Installs the BCC component library so PrimeVue puts the CSP nonce on the theme <style> elements it injects. The
 * library installs PrimeVue without a csp option, and PrimeVue injects its theme during that install, so the nonce is
 * added to the options the library passes to app.use: the PrimeVue config is the one with a theme. csp.test.ts fails if
 * a library update changes this. Remove once the library takes a csp option
 * (docs/tech-debt/bcc-component-library-takes-no-csp-nonce.md).
 */
export function installBccComponentLibrary(app: App, nonce: string | undefined) {
  const original = app.use
  const use = original as (plugin: unknown, ...options: unknown[]) => App
  app.use = ((plugin: unknown, ...options: unknown[]) =>
    use.call(app, plugin, ...options.map((o) => withNonce(o, nonce)))) as App['use']
  try {
    app.use(BccComponentLibrary)
  } finally {
    app.use = original
  }
}

function withNonce(options: unknown, nonce: string | undefined) {
  return nonce && typeof options === 'object' && options !== null && 'theme' in options ? { ...options, csp: { nonce } } : options
}
