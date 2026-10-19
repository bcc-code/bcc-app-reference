<script setup lang="ts">
import { useQuery } from '@tanstack/vue-query'
import { useI18n } from 'vue-i18n'
import ProblemMessage from '@/components/ProblemMessage.vue'
import { statsQueryOptions } from './queries'

const { t, n } = useI18n()
const { data: stats, error, isPending } = useQuery(statsQueryOptions())
</script>

<template>
  <section class="flex flex-col gap-4">
    <h1 class="m-0 text-2xl font-semibold">{{ t('admin.stats.title') }}</h1>
    <p v-if="isPending" role="status">{{ t('common.loading') }}</p>
    <ProblemMessage :error="error" />
    <dl v-if="stats" class="m-0 grid max-w-md grid-cols-2 gap-4">
      <div class="rounded-lg border border-(--color-border-default) bg-white p-4">
        <dt class="text-sm">{{ t('admin.stats.activeNotes') }}</dt>
        <dd class="m-0 text-3xl font-semibold" data-testid="stats-active">{{ n(stats.activeNotes) }}</dd>
      </div>
      <div class="rounded-lg border border-(--color-border-default) bg-white p-4">
        <dt class="text-sm">{{ t('admin.stats.archivedNotes') }}</dt>
        <dd class="m-0 text-3xl font-semibold" data-testid="stats-archived">{{ n(stats.archivedNotes) }}</dd>
      </div>
    </dl>
  </section>
</template>
