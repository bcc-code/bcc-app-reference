export const LAYER_ORDER = '@layer theme, base, components, library-utilities, primevue, custom, utilities;'

/** PrimeVue prepends its own @layer statement, so the app's order is re-asserted as the first <style> in <head>. */
export function assertLayerOrder(doc: Document = document): void {
  const style = doc.createElement('style')
  style.dataset.templateLayerOrder = ''
  style.textContent = LAYER_ORDER
  const putFirst = () => {
    if (doc.head.firstChild !== style) doc.head.insertBefore(style, doc.head.firstChild)
  }
  putFirst()
  new MutationObserver(putFirst).observe(doc.head, { childList: true })
}
