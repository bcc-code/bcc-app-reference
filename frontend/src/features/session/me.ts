import { queryOptions } from '@tanstack/vue-query'
import { getMe, type MeResponse } from '@/client'

export const ADMIN_ROLE = 'admin'

/** The signed-in user. The server decides what the user may do; the roles only decide what the SPA shows. */
export const meQueryOptions = () =>
  queryOptions({
    queryKey: ['me'] as const,
    queryFn: async ({ signal }) => (await getMe({ signal })).data,
    staleTime: 5 * 60 * 1000,
  })

export function hasRole(me: Pick<MeResponse, 'roles'> | undefined, role: string): boolean {
  return me?.roles.includes(role) ?? false
}
