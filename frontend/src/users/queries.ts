import { queryOptions } from '@tanstack/vue-query'
import { UsersClient } from '@/client'

export const usersQueries = {
  /** The signed-in user. The server decides what the user may do; the roles only decide what the SPA shows. */
  me: () =>
    queryOptions({
      queryKey: ['users', 'me'],
      queryFn: async ({ signal }) => (await UsersClient.getMe({ signal })).data,
      staleTime: 5 * 60 * 1000,
    }),
}
