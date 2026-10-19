import { queryOptions } from '@tanstack/vue-query'
import { getStats } from '@/client'

export const statsQueryOptions = () =>
  queryOptions({
    queryKey: ['admin', 'stats'] as const,
    queryFn: async ({ signal }) => (await getStats({ signal })).data,
  })
