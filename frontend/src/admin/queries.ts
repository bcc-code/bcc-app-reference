import { queryOptions } from '@tanstack/vue-query'
import { AdminClient } from '@/client'

export const adminQueries = {
  stats: () =>
    queryOptions({
      queryKey: ['admin', 'stats'],
      queryFn: async ({ signal }) => (await AdminClient.getStats({ signal })).data,
    }),
}
