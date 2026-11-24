<script setup lang="ts">
import { useI18n } from 'vue-i18n'

const { t } = useI18n()
</script>

<template>
  <section class="flex flex-col gap-4" data-testid="no-access">
    <h1 class="m-0 heading-xl">{{ t('noAccess.title') }}</h1>
    <p class="m-0">{{ t('noAccess.text') }}</p>
    <p class="m-0">
      <RouterLink to="/notes">{{ t('noAccess.home') }}</RouterLink>
    </p>
  </section>
</template>
