<script setup lang="ts">
import { BccButton } from '@bcc-code/component-library-vue'
import { useQuery } from '@tanstack/vue-query'
import { useI18n } from 'vue-i18n'
import ProblemMessage from '@/components/ProblemMessage.vue'
import { notesQueryOptions, useArchiveNote } from './queries'

const { t, d } = useI18n()
const { data: notes, error: loadError, isPending: loading } = useQuery(notesQueryOptions())
const { mutate: archive, error: archiveError, variables: archiving } = useArchiveNote()
</script>

<template>
  <section class="flex flex-col gap-4">
    <div class="flex flex-wrap items-center justify-between gap-2">
      <h1 class="m-0 text-2xl font-semibold">{{ t('notes.title') }}</h1>
      <RouterLink
        to="/notes/new"
        class="inline-flex min-h-10 items-center rounded-lg bg-(--color-bcc-800) px-4 text-sm font-medium text-white no-underline"
        >{{ t('notes.new') }}</RouterLink
      >
    </div>
    <p v-if="loading" role="status">{{ t('common.loading') }}</p>
    <ProblemMessage :error="loadError" />
    <ProblemMessage :error="archiveError" />
    <p v-if="notes?.length === 0" class="m-0">{{ t('notes.empty') }}</p>
    <ul v-else class="m-0 flex list-none flex-col gap-3 p-0">
      <li
        v-for="note in notes"
        :key="note.id"
        class="flex flex-wrap items-start justify-between gap-3 rounded-lg border border-(--color-border-default) bg-white p-4"
        :aria-busy="archiving?.id === note.id"
      >
        <div class="min-w-0 flex-1">
          <h2 class="m-0 text-lg font-medium">
            <RouterLink :to="`/notes/${note.id}/edit`" class="underline" :aria-label="t('notes.edit', { title: note.title })">{{
              note.title
            }}</RouterLink>
          </h2>
          <p class="m-0 mt-1 line-clamp-2 whitespace-pre-line">{{ note.body }}</p>
          <p class="m-0 mt-1 text-sm text-(--color-text-subtle)">
            {{ t('notes.updated', { date: d(new Date(note.updatedAt), 'short') }) }}
          </p>
        </div>
        <BccButton
          type="button"
          severity="secondary"
          variant="outlined"
          size="small"
          :label="t('notes.archive', { title: note.title })"
          @click="archive(note)"
        />
      </li>
    </ul>
  </section>
</template>
