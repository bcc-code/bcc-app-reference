<script setup lang="ts">
import { useQuery } from '@tanstack/vue-query'
import { useI18n } from 'vue-i18n'
import UserMenu from '@/components/UserMenu.vue'
import { ADMIN_ROLE, hasRole, meQueryOptions } from '@/features/session/me'

const { t } = useI18n()
const { data: me } = useQuery(meQueryOptions())
</script>

<template>
  <div class="min-h-screen bg-(--color-elevation-surface-sunken-default)">
    <a href="#main" class="sr-only focus:not-sr-only focus:absolute focus:z-30 focus:bg-white focus:p-2">{{
      t('app.skipToContent')
    }}</a>
    <header class="bg-(--color-bcc-800) text-white">
      <div class="mx-auto flex min-h-14 max-w-5xl flex-wrap items-center gap-x-4 gap-y-1 px-4 py-1 sm:px-8">
        <RouterLink to="/notes" class="text-lg font-semibold text-white no-underline">{{ t('app.title') }}</RouterLink>
        <nav :aria-label="t('nav.label')" class="flex grow gap-4">
          <RouterLink to="/notes" class="text-sm font-medium text-white">{{ t('nav.notes') }}</RouterLink>
          <RouterLink v-if="hasRole(me, ADMIN_ROLE)" to="/admin/stats" class="text-sm font-medium text-white">{{
            t('nav.stats')
          }}</RouterLink>
        </nav>
        <UserMenu />
      </div>
    </header>
    <main id="main" class="mx-auto max-w-5xl px-4 py-6 sm:px-8 sm:py-8">
      <RouterView />
    </main>
  </div>
</template>
