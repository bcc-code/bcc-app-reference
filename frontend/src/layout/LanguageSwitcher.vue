<script setup lang="ts">
import { BccSelect } from '@bcc-code/component-library-vue'
import { useI18n } from 'vue-i18n'
import type { Language } from '@/i18n'
import { rememberLanguage } from '@/i18n/locale'

const { t, locale } = useI18n()

// Each language in its own name.
const languages: { value: Language; label: string }[] = [
  { value: 'nb', label: 'Norsk' },
  { value: 'en', label: 'English' },
]

function choose(value: Language) {
  locale.value = value
  rememberLanguage(value)
}
</script>

<template>
  <BccSelect
    class="w-36"
    :model-value="locale"
    :options="languages"
    option-label="label"
    option-value="value"
    :aria-label="t('layout.language')"
    @update:model-value="choose"
  />
</template>
