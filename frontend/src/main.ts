import { VueQueryPlugin } from '@tanstack/vue-query'
import { createApp } from 'vue'
import App from './App.vue'
import { client } from './client/client.gen'
import { cspNonce, installBccComponentLibrary } from './csp'
import { createTemplateI18n, pickLanguage } from './i18n'
import { rememberedLanguage } from './i18n/locale'
import { withStatus } from './lib/problems'
import { queryClient } from './queryClient'
import { createAppRouter } from './router'
import { assertLayerOrder } from './style/layerOrder'
import './style/main.css'

// The client throws an empty object for a 403 and a 404, which have no body: keep their status for the messages.
client.interceptors.error.use(withStatus)

// Runtime settings come from the server (/api/me), never from build-time variables or secrets.
const router = createAppRouter(queryClient)
const app = createApp(App)
const nonce = cspNonce()
installBccComponentLibrary(app, nonce)
assertLayerOrder(nonce)
const i18n = createTemplateI18n(rememberedLanguage() ?? pickLanguage([...navigator.languages, document.documentElement.lang]))
document.documentElement.lang = i18n.global.locale.value
app.use(i18n)
app.use(router)
app.use(VueQueryPlugin, { queryClient })
void router.isReady().then(() => app.mount('#app'))
