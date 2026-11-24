<script setup lang="ts">
import type { Component } from 'vue'
import { RouterLink } from 'vue-router'

// A nested link has no icon and sits under its parent's text. A parent with nested links is `exact`, so it is not
// highlighted along with the one under it that is open.
const {
  to,
  icon,
  exact = false,
  nested = false,
} = defineProps<{ to: string; icon?: Component; exact?: boolean; nested?: boolean }>()
</script>

<!-- The design system's navigation item: medium weight, the small radius, the selected tokens. -->
<template>
  <RouterLink v-slot="{ isActive, isExactActive, href, navigate }" :to="to" custom>
    <a
      :href="href"
      class="flex items-center gap-3 rounded-sm px-3 py-2 label-md no-underline transition hover:bg-interaction-hovered"
      :class="[
        (exact ? isExactActive : isActive) ? 'bg-selected-default text-selected' : 'text-subtle hover:text-default',
        { 'pl-12': nested },
      ]"
      :aria-current="(exact ? isExactActive : isActive) ? 'page' : undefined"
      @click="navigate"
    >
      <component :is="icon" v-if="icon" class="h-6 w-6" aria-hidden="true" />
      <span><slot /></span>
    </a>
  </RouterLink>
</template>
