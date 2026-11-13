export const LAYER_ORDER = '@layer theme, base, components, library-utilities, primevue, custom, utilities;'

/**
 * PrimeVue prepends its own @layer statement, so the app's order is re-asserted as the first <style> in <head>. The nonce is the
 * page's CSP nonce; without it the shell CSP blocks the element.
 */
export function assertLayerOrder(nonce: string | undefined, doc: Document = document): void {
  const style = doc.createElement('style')
  style.dataset.templateLayerOrder = ''
  if (nonce) style.setAttribute('nonce', nonce)
  style.textContent = LAYER_ORDER
  const putFirst = () => {
    if (doc.head.firstChild !== style) doc.head.insertBefore(style, doc.head.firstChild)
  }
  putFirst()
  new MutationObserver(putFirst).observe(doc.head, { childList: true })
}
