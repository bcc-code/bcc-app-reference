<script setup lang="ts">
import { BarChartIcon, DescriptionIcon } from '@bcc-code/icons-vue'
import { useQuery } from '@tanstack/vue-query'
import { useI18n } from 'vue-i18n'
import { mayOpenStats } from '@/admin/rules'
import SignOutForm from '@/users/SignOutForm.vue'
import { usersQueries } from '@/users/queries'
import LanguageSwitcher from './LanguageSwitcher.vue'
import MenuLink from './MenuLink.vue'

const { t } = useI18n()
// The server requires sign-in before it serves the page, so there is no sign-in button; the name shows once loaded.
const { data: user } = useQuery(usersQueries.me())
</script>

<!-- Two rows, because the app has two pages: the BCC brand colour with the title, the language and the person, and the
     menu under it. A sidebar (bcc-ateam's) would be empty space here. -->
<template>
  <header class="print:hidden">
    <div class="flex flex-wrap items-center gap-x-4 gap-y-2 bg-brand-bolder-default px-4 py-3 md:px-6">
      <RouterLink to="/notes" class="heading-xl text-inverse no-underline hover:text-inverse">{{ t('app.title') }}</RouterLink>
      <div class="ml-auto flex flex-wrap items-center gap-3">
        <LanguageSwitcher />
        <span v-if="user" class="body-md text-inverse" data-testid="current-user-name">{{ user.displayName }}</span>
        <SignOutForm v-if="user" />
      </div>
    </div>
    <nav :aria-label="t('nav.label')" class="flex flex-wrap gap-1 bg-elevation-surface-sunken-default px-4 py-2 md:px-6">
      <MenuLink to="/notes" :icon="DescriptionIcon">{{ t('nav.notes') }}</MenuLink>
      <MenuLink v-if="user && mayOpenStats(user.roles)" to="/admin/stats" :icon="BarChartIcon">{{ t('nav.stats') }}</MenuLink>
    </nav>
  </header>
</template>
