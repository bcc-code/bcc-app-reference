<script setup lang="ts">
import { ErrorIcon } from '@bcc-code/icons-vue'

/** What is wrong with a field, under it in the danger colour. The field points at the id with aria-describedby. */
const { id, errors } = defineProps<{ id: string; errors: readonly string[] }>()
</script>

<template>
  <div v-if="errors.length" :id="id" class="flex flex-col gap-1">
    <p v-for="(error, i) in errors" :key="i" class="flex items-center gap-1 body-sm text-danger">
      <ErrorIcon class="size-4 shrink-0" aria-hidden="true" />{{ error }}
    </p>
  </div>
</template>
