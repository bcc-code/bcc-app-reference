<script setup lang="ts">
import { BccMessage } from '@bcc-code/component-library-vue'
import { computed } from 'vue'
import { useI18n } from 'vue-i18n'
import { problemKeys } from '@/lib/problems'

const { error } = defineProps<{ error: unknown }>()
const { t } = useI18n()
const messages = computed(() => (error ? problemKeys(error).map((key) => t(key)) : []))
</script>

<template>
  <BccMessage v-if="messages.length > 0" severity="error" role="alert" data-testid="problem">
    <ul class="m-0 list-none p-0">
      <li v-for="message in messages" :key="message">{{ message }}</li>
    </ul>
  </BccMessage>
</template>
