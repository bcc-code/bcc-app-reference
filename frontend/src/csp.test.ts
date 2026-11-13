import { BccCard } from '@bcc-code/component-library-vue'
import { createApp, h } from 'vue'
import { describe, expect, it } from 'vitest'
import { installBccComponentLibrary } from './csp'
import { assertLayerOrder } from './style/layerOrder'

// Runs the real component library: if an update changes how it installs PrimeVue, the styles lose the nonce and the
// shell CSP (style-src 'nonce-…') blocks them in the browser. PrimeVue injects theme styles at install and each
// component's styles when it first mounts, so the app renders one component.
describe('CSP nonce', () => {
  it('is on every style the app and PrimeVue inject', () => {
    const app = createApp({ render: () => h(BccCard) })

    installBccComponentLibrary(app, 'test-nonce')
    assertLayerOrder('test-nonce')
    app.mount(document.createElement('div'))

    const styles = [...document.querySelectorAll('style')]
    expect(styles.map((s) => s.getAttribute('data-primevue-style-id'))).toContain('card-style')
    expect(styles.map((s) => s.getAttribute('nonce'))).toEqual(styles.map(() => 'test-nonce'))
    app.unmount()
  })
})
