<script setup lang="ts">
import { useI18n } from 'vue-i18n'
import AppTopbar from './layout/AppTopbar.vue'

const { t } = useI18n()
</script>

<template>
  <div class="min-h-screen bg-elevation-surface-default body-lg text-default">
    <a href="#main" class="sr-only focus:not-sr-only focus:absolute focus:z-30 focus:bg-white focus:p-2">{{
      t('app.skipToContent')
    }}</a>
    <AppTopbar />
    <main id="main" class="mx-auto max-w-5xl px-4 py-6 sm:px-8 sm:py-8">
      <RouterView />
    </main>
  </div>
</template>
