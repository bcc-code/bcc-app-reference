<script setup lang="ts">
import { computed, useId } from 'vue'
import FieldErrors from './FieldErrors.vue'

/**
 * A field laid out as the BCC design system lays one out: the label above at label-md with a mark when it is required,
 * the hint and the errors below, both tied to the input with aria-describedby, 8px apart. The slot gets the ids to put on
 * its input, and whether it is required for aria-required; a select names itself with labelId instead. A label with
 * links comes through the label slot. The mark sits next to the label rather than in it, so the field's name is the label
 * alone and the mark is not read out as a star.
 */
const {
  label = '',
  hint,
  required = false,
  errors = [],
  labelledby = false,
} = defineProps<{
  label?: string
  hint?: string
  required?: boolean
  errors?: readonly string[]
  /** The control names itself with labelId (a select's combobox), so the label has no for to point with. */
  labelledby?: boolean
}>()
const id = useId()
const labelId = `${id}-label`
const hintId = `${id}-hint`
const errorId = `${id}-error`
const describedBy = computed(() => [hint ? hintId : null, errors.length ? errorId : null].filter(Boolean).join(' ') || undefined)
</script>

<template>
  <div class="flex flex-col gap-2" :data-invalid="errors.length > 0 || undefined">
    <div class="flex items-baseline gap-1">
      <label :id="labelId" :for="labelledby ? undefined : id" class="label-md"
        ><slot name="label">{{ label }}</slot></label
      >
      <span v-if="required" class="label-md text-danger" aria-hidden="true">*</span>
    </div>
    <slot v-bind="{ id, labelId, describedBy, required, invalid: errors.length > 0 }" />
    <p v-if="hint" :id="hintId" class="body-sm text-subtle">{{ hint }}</p>
    <FieldErrors :id="errorId" :errors="errors" />
  </div>
</template>
