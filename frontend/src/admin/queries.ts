import { queryOptions, useMutation, useQueryClient } from '@tanstack/vue-query'
import { AdminClient } from '@/client'

export const adminQueries = {
  stats: () =>
    queryOptions({
      queryKey: ['admin', 'stats'],
      queryFn: async ({ signal }) => (await AdminClient.getStats({ signal })).data,
    }),
  /** Every query of the people list, to invalidate after a grant or a revoke. */
  allPeople: () => ['admin', 'people'] as const,
  people: (search: string) =>
    queryOptions({
      queryKey: [...adminQueries.allPeople(), search],
      queryFn: async ({ signal }) =>
        (await AdminClient.listPeople({ query: search ? { search } : undefined, signal })).data.people,
    }),
}

/** Both are set-state calls, so there is no version to send; the list is refreshed whatever the outcome. */
export function useGrantAdmin() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: async (personId: string) => AdminClient.grantAdmin({ path: { personId } }),
    onSettled: () => queryClient.invalidateQueries({ queryKey: adminQueries.allPeople() }),
  })
}

export function useRevokeAdmin() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: async (personId: string) => AdminClient.revokeAdmin({ path: { personId } }),
    onSettled: () => queryClient.invalidateQueries({ queryKey: adminQueries.allPeople() }),
  })
}
