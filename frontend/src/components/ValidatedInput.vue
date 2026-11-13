<script setup lang="ts">
import { BccInput } from '@bcc-code/component-library-vue'
import { computed } from 'vue'
import { useI18n } from 'vue-i18n'
import FormField from './FormField.vue'
import { type FieldValidation, errorMessages, isRequired } from './validation'

const {
  label,
  validators = undefined,
  type = 'text',
  readonly = false,
  serverInvalid = false,
  max = undefined,
  autocomplete = undefined,
} = defineProps<{
  label: string
  validators?: FieldValidation
  type?: string
  readonly?: boolean
  serverInvalid?: boolean
  max?: string
  autocomplete?: string
}>()
const model = defineModel<string | null | undefined>()

const { t } = useI18n()
const errors = computed(() => errorMessages(validators, serverInvalid).map((message) => t(message.key)))
</script>

<template>
  <FormField :label="label" :required="isRequired(validators)" :errors="errors">
    <template v-if="$slots.label" #label><slot name="label" /></template>
    <template #default="{ id, describedBy, required, invalid }">
      <BccInput
        :id="id"
        v-model="model"
        class="w-full"
        :type="type"
        :readonly="readonly"
        :max="max"
        :autocomplete="autocomplete"
        :aria-describedby="describedBy"
        :aria-required="required || undefined"
        :invalid="invalid"
      />
    </template>
  </FormField>
</template>
