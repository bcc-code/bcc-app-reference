import { BccComponentLibrary } from '@bcc-code/component-library-vue'
import { QueryClient, VueQueryPlugin } from '@tanstack/vue-query'
import { createApp } from 'vue'
import App from './App.vue'
import { client } from './client/client.gen'
import { createTemplateI18n, pickLanguage } from './i18n'
import { templateFetch } from './lib/templateFetch'
import { createAppRouter } from './router'
import { assertLayerOrder } from './style/layerOrder'
import './style/main.css'

// All API traffic goes through the one wrapper: CSRF token, one retry on a stale token, and login on 401.
client.setConfig({ fetch: templateFetch, throwOnError: true })

// Runtime settings come from the server (/api/me), never from build-time variables or secrets.
const queryClient = new QueryClient({ defaultOptions: { queries: { staleTime: 30_000, retry: 1 } } })
const router = createAppRouter(queryClient)
const app = createApp(App)
app.use(BccComponentLibrary)
assertLayerOrder()
const i18n = createTemplateI18n(pickLanguage([...navigator.languages, document.documentElement.lang]))
document.documentElement.lang = i18n.global.locale.value
app.use(i18n)
app.use(router)
app.use(VueQueryPlugin, { queryClient })
void router.isReady().then(() => app.mount('#app'))
