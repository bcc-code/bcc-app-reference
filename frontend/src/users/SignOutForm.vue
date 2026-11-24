<script setup lang="ts">
import { BccButton } from '@bcc-code/component-library-vue'
import { useI18n } from 'vue-i18n'

const { t } = useI18n()
</script>

<template>
  <!-- A plain form post, not a fetch: logout redirects through the identity provider, which fetch cannot follow. The server
       refuses a post that another site caused, so the form carries no token. -->
  <form method="post" action="/auth/logout">
    <BccButton type="submit" size="small" severity="secondary" :label="t('users.signOut')" />
  </form>
</template>
