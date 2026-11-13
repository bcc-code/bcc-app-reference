<script setup lang="ts">
import { BccTextarea } from '@bcc-code/component-library-vue'
import { computed } from 'vue'
import { useI18n } from 'vue-i18n'
import FormField from './FormField.vue'
import { type FieldValidation, errorMessages, isRequired } from './validation'

const {
  label,
  validators = undefined,
  rows = 4,
  serverInvalid = false,
} = defineProps<{ label: string; validators?: FieldValidation; rows?: number; serverInvalid?: boolean }>()
const model = defineModel<string | null | undefined>()

const { t } = useI18n()
const errors = computed(() => errorMessages(validators, serverInvalid).map((message) => t(message.key)))
</script>

<template>
  <FormField :label="label" :required="isRequired(validators)" :errors="errors">
    <template v-if="$slots.label" #label><slot name="label" /></template>
    <template #default="{ id, describedBy, required, invalid }">
      <BccTextarea
        :id="id"
        v-model="model"
        class="w-full"
        :rows="rows"
        :aria-describedby="describedBy"
        :aria-required="required || undefined"
        :invalid="invalid"
      />
    </template>
  </FormField>
</template>
