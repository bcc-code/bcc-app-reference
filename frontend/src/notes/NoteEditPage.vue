<script setup lang="ts">
import { BccButton } from '@bcc-code/component-library-vue'
import { useQuery } from '@tanstack/vue-query'
import { computed } from 'vue'
import { useI18n } from 'vue-i18n'
import { useRouter } from 'vue-router'
import ProblemMessage from '@/components/ProblemMessage.vue'
import { isConflict } from '@/lib/problems'
import NoteForm from './NoteForm.vue'
import { notesQueries, useEditNote, type NoteDraft } from './queries'

const { noteId } = defineProps<{ noteId: string }>()
const { t } = useI18n()
const router = useRouter()
const {
  data: note,
  error: loadError,
  isPending: loading,
  refetch,
  isFetching,
} = useQuery(computed(() => notesQueries.detail(noteId)))
const { mutate, reset, isPending, error } = useEditNote(noteId)

function save(draft: NoteDraft) {
  if (note.value) mutate({ ...draft, version: note.value.version }, { onSuccess: () => router.push('/notes') })
}

async function reload() {
  await refetch()
  reset()
}
</script>

<template>
  <section class="flex flex-col gap-4">
    <h1 class="m-0 heading-xl">{{ t('notes.editTitle') }}</h1>
    <p v-if="loading" role="status">{{ t('common.loading') }}</p>
    <ProblemMessage :error="loadError" />
    <!-- Keyed by version: loading the latest version after a 409 replaces the fields with the saved note. -->
    <NoteForm
      v-if="note"
      :key="note.version"
      :initial="{ title: note.title, body: note.body }"
      :submit-label="t('notes.form.save')"
      :pending="isPending"
      :error="error"
      @submit="save"
    >
      <template v-if="isConflict(error)" #actions>
        <BccButton type="button" severity="secondary" :label="t('notes.reload')" :loading="isFetching" @click="reload" />
      </template>
    </NoteForm>
    <p class="m-0">
      <RouterLink to="/notes">{{ t('common.back') }}</RouterLink>
    </p>
  </section>
</template>
