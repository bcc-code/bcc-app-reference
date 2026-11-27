<script setup lang="ts">
import { BccButton, BccInput } from '@bcc-code/component-library-vue'
import { useQuery } from '@tanstack/vue-query'
import { computed, ref } from 'vue'
import { useI18n } from 'vue-i18n'
import ProblemMessage from '@/components/ProblemMessage.vue'
import { adminQueries, useGrantAdmin, useRevokeAdmin } from './queries'
import { providerLabelKey } from './rules'

const { t, d } = useI18n()
// The list asks for the search that was submitted, not for every keystroke.
const draft = ref('')
const search = ref('')
const { data: people, error: loadError, isPending: loading } = useQuery(computed(() => adminQueries.people(search.value)))
const { mutate: grant, error: grantError, variables: granting } = useGrantAdmin()
const { mutate: revoke, error: revokeError, variables: revoking } = useRevokeAdmin()

function submit() {
  search.value = draft.value.trim()
}

const nameOf = (name: string) => name || t('admin.people.unnamed')
</script>

<template>
  <section class="flex flex-col gap-4" aria-labelledby="people-title">
    <h2 id="people-title" class="m-0 heading-lg">{{ t('admin.people.title') }}</h2>
    <p class="m-0 body-sm text-subtle">{{ t('admin.people.grantNote') }}</p>
    <form class="flex max-w-md items-center gap-2" role="search" @submit.prevent="submit">
      <BccInput
        v-model="draft"
        class="min-w-0 flex-1"
        type="search"
        :aria-label="t('admin.people.search')"
        :placeholder="t('admin.people.search')"
        data-testid="people-search"
      />
      <BccButton type="submit" severity="secondary" :label="t('admin.people.searchButton')" />
    </form>
    <p v-if="loading" role="status">{{ t('common.loading') }}</p>
    <ProblemMessage :error="loadError" />
    <ProblemMessage :error="grantError" />
    <ProblemMessage :error="revokeError" />
    <p v-if="people?.length === 0" class="m-0">{{ t('admin.people.empty') }}</p>
    <div v-else-if="people" class="overflow-x-auto">
      <table class="w-full border-collapse text-left" data-testid="people">
        <thead>
          <tr class="border-b border-default">
            <th scope="col" class="p-2 label-md">{{ t('admin.people.columns.name') }}</th>
            <th scope="col" class="p-2 label-md">{{ t('admin.people.columns.provider') }}</th>
            <th scope="col" class="p-2 label-md">{{ t('admin.people.columns.lastSignIn') }}</th>
            <th scope="col" class="p-2 label-md">{{ t('admin.people.columns.admin') }}</th>
            <th scope="col" class="p-2 label-md">
              <span class="sr-only">{{ t('admin.people.columns.actions') }}</span>
            </th>
          </tr>
        </thead>
        <tbody>
          <tr
            v-for="person in people"
            :key="`${person.personId}-${person.provider}`"
            class="border-b border-default"
            :aria-busy="granting === person.personId || revoking === person.personId"
          >
            <th scope="row" class="p-2 font-normal">{{ nameOf(person.displayName) }}</th>
            <td class="p-2">{{ t(providerLabelKey(person.provider)) }}</td>
            <td class="p-2">{{ d(new Date(person.lastSignInAt), 'short') }}</td>
            <td class="p-2">{{ person.isAdmin ? t('admin.people.yes') : t('admin.people.no') }}</td>
            <td class="p-2 text-right">
              <BccButton
                v-if="person.isAdmin"
                type="button"
                severity="secondary"
                variant="outlined"
                size="small"
                :label="t('admin.people.revokeFor', { name: nameOf(person.displayName) })"
                @click="revoke(person.personId)"
              />
              <BccButton
                v-else
                type="button"
                severity="secondary"
                variant="outlined"
                size="small"
                :label="t('admin.people.grantFor', { name: nameOf(person.displayName) })"
                @click="grant(person.personId)"
              />
            </td>
          </tr>
        </tbody>
      </table>
    </div>
  </section>
</template>
