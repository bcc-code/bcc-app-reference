<script setup lang="ts">
import { useI18n } from 'vue-i18n'

const { t } = useI18n()
</script>

<template>
  <section class="flex flex-col gap-4">
    <h1 class="m-0 text-2xl font-semibold">{{ t('notFound.title') }}</h1>
    <p class="m-0">{{ t('notFound.text') }}</p>
    <p class="m-0">
      <RouterLink to="/notes">{{ t('notFound.home') }}</RouterLink>
    </p>
  </section>
</template>
