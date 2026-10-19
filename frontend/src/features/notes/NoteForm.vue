<script setup lang="ts">
import { BccButton, BccInput, BccTextarea } from '@bcc-code/component-library-vue'
import { ref, useId } from 'vue'
import { useI18n } from 'vue-i18n'
import ProblemMessage from '@/components/ProblemMessage.vue'
import type { NoteDraft } from './queries'

/** The fields and buttons both the create and the edit page use. The pages own loading, saving and navigation. */
const {
  initial = { title: '', body: '' },
  submitLabel,
  pending,
  error,
} = defineProps<{
  initial?: NoteDraft
  submitLabel: string
  pending: boolean
  error: unknown
}>()
const emit = defineEmits<{ submit: [draft: NoteDraft] }>()

const { t } = useI18n()
const id = useId()
const title = ref(initial.title)
const body = ref(initial.body)
</script>

<template>
  <form class="flex max-w-2xl flex-col gap-4" @submit.prevent="emit('submit', { title, body })">
    <div class="flex flex-col gap-1">
      <label :for="`${id}-title`" class="text-sm font-medium">{{ t('notes.form.title') }}</label>
      <BccInput :id="`${id}-title`" v-model="title" name="title" required maxlength="200" autocomplete="off" />
    </div>
    <div class="flex flex-col gap-1">
      <label :for="`${id}-body`" class="text-sm font-medium">{{ t('notes.form.body') }}</label>
      <BccTextarea :id="`${id}-body`" v-model="body" name="body" rows="8" maxlength="10000" />
    </div>
    <ProblemMessage :error="error" />
    <div class="flex flex-wrap gap-2">
      <BccButton type="submit" :label="submitLabel" :loading="pending" />
      <slot name="actions" />
    </div>
  </form>
</template>
