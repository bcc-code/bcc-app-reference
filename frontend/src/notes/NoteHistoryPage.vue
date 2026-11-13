<script setup lang="ts">
import { useQuery } from '@tanstack/vue-query'
import { useI18n } from 'vue-i18n'
import ProblemMessage from '@/components/ProblemMessage.vue'
import { notesQueries } from './queries'

const { noteId } = defineProps<{ noteId: string }>()
const { t, te, d } = useI18n()
const { data: entries, error, isPending } = useQuery(notesQueries.history(noteId))

// The business steps the API names (NoteCreated, NoteEdited, NoteArchived) have a message each; a step added later shows its name.
const stepLabel = (step: string) => (te(`notes.history.steps.${step}`) ? t(`notes.history.steps.${step}`) : step)
</script>

<template>
  <section class="flex flex-col gap-4">
    <h1 class="m-0 heading-xl">{{ t('notes.history.title') }}</h1>
    <p v-if="isPending" role="status">{{ t('common.loading') }}</p>
    <ProblemMessage :error="error" />
    <ol v-if="entries" class="m-0 flex list-none flex-col gap-3 p-0">
      <li
        v-for="entry in entries"
        :key="entry.version"
        class="flex flex-col gap-1 rounded-lg border border-default bg-elevation-surface-default p-4"
      >
        <p class="m-0 label-md">{{ stepLabel(entry.step) }}</p>
        <p class="m-0">{{ entry.title }}</p>
        <p v-if="entry.comment" class="m-0 whitespace-pre-line">{{ entry.comment }}</p>
        <p class="m-0 body-sm text-subtle">
          {{ t('notes.history.version', { version: entry.version }) }} · {{ d(new Date(entry.at), 'short') }}
        </p>
      </li>
    </ol>
    <p class="m-0">
      <RouterLink to="/notes">{{ t('common.back') }}</RouterLink>
    </p>
  </section>
</template>
