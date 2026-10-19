<script setup lang="ts">
import { BccButton } from '@bcc-code/component-library-vue'
import { useQuery } from '@tanstack/vue-query'
import { nextTick, ref, useId } from 'vue'
import { useI18n } from 'vue-i18n'
import { meQueryOptions } from '@/features/session/me'
import { LOGOUT_TOKEN_FIELD, LOGOUT_URL, logoutToken } from '@/lib/logout'

const { t } = useI18n()
const { data: me } = useQuery(meQueryOptions())
const open = ref(false)
const panelId = useId()
const form = ref<HTMLFormElement | null>(null)
const token = ref('')

// A plain form post, not a fetch: logout redirects through the identity provider. The token is the XSRF cookie, read when
// the user submits so it is never older than the page.
async function signOut() {
  token.value = await logoutToken({ fetch: (input, init) => fetch(input, init), cookies: () => document.cookie })
  // The hidden field is bound to `token`; without waiting for the re-render the form would post the old (empty) value.
  await nextTick()
  form.value?.submit()
}
</script>

<template>
  <div v-if="me" class="relative" @keydown.esc="open = false">
    <button
      type="button"
      class="flex min-h-10 cursor-pointer items-center gap-2 rounded border border-white/60 bg-transparent px-3 text-sm font-medium text-white"
      :aria-expanded="open"
      :aria-controls="panelId"
      :aria-label="t('user.menu', { name: me.displayName })"
      data-testid="user-menu"
      @click="open = !open"
    >
      <span>{{ me.displayName }}</span>
    </button>
    <div
      v-show="open"
      :id="panelId"
      class="absolute right-0 z-20 mt-2 flex min-w-56 flex-col gap-3 rounded-lg border border-(--color-border-default) bg-white p-4 text-(--color-text-default) shadow-lg"
    >
      <p class="m-0 text-sm">{{ t('user.signedInAs', { name: me.displayName }) }}</p>
      <form ref="form" method="post" :action="LOGOUT_URL" @submit.prevent="signOut">
        <input type="hidden" :name="LOGOUT_TOKEN_FIELD" :value="token" />
        <BccButton type="submit" severity="secondary" size="small" :label="t('user.logout')" data-testid="logout" />
      </form>
    </div>
  </div>
</template>
