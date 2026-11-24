<script setup lang="ts">
import { useI18n } from 'vue-i18n'
import { useRouter } from 'vue-router'
import NoteForm from './NoteForm.vue'
import { useCreateNote, type NoteDraft } from './queries'

const { t } = useI18n()
const router = useRouter()
const { mutate, isPending, error } = useCreateNote()

function create(draft: NoteDraft) {
  mutate(draft, { onSuccess: () => router.push('/notes') })
}
</script>

<template>
  <section class="flex flex-col gap-4">
    <h1 class="m-0 heading-xl">{{ t('notes.createTitle') }}</h1>
    <NoteForm :submit-label="t('notes.form.create')" :pending="isPending" :error="error" @submit="create" />
    <p class="m-0">
      <RouterLink to="/notes">{{ t('common.back') }}</RouterLink>
    </p>
  </section>
</template>
