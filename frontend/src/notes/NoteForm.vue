<script setup lang="ts">
import { BccButton } from '@bcc-code/component-library-vue'
import { useVuelidate } from '@vuelidate/core'
import { maxLength, required } from '@vuelidate/validators'
import { reactive } from 'vue'
import ProblemMessage from '@/components/ProblemMessage.vue'
import ValidatedInput from '@/components/ValidatedInput.vue'
import ValidatedTextarea from '@/components/ValidatedTextarea.vue'
import { useI18n } from 'vue-i18n'
import type { NoteDraft } from './queries'

/**
 * The fields and buttons both the create and the edit page use. The pages own loading, saving and navigation. The rules
 * here only save a round trip: the server validates again (NoteContent.Parse) and its answer is shown under the fields.
 */
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
const form = reactive({ title: initial.title, body: initial.body })
// The rule names are the form.alerts keys of their messages. $autoDirty shows a message as soon as the field has changed.
const v$ = useVuelidate({ title: { required, maxLength200: maxLength(200) }, body: { maxLength10000: maxLength(10000) } }, form, {
  $autoDirty: true,
})

async function submit() {
  if (await v$.value.$validate()) emit('submit', { title: form.title, body: form.body })
}
</script>

<template>
  <form class="flex max-w-2xl flex-col gap-4" novalidate @submit.prevent="submit">
    <ValidatedInput v-model="form.title" :label="t('notes.form.title')" :validators="v$.title" autocomplete="off" />
    <ValidatedTextarea v-model="form.body" :label="t('notes.form.body')" :validators="v$.body" :rows="8" />
    <ProblemMessage :error="error" />
    <div class="flex flex-wrap gap-2">
      <BccButton type="submit" :label="submitLabel" :loading="pending" />
      <slot name="actions" />
    </div>
  </form>
</template>
