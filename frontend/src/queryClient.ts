import { QueryClient } from '@tanstack/vue-query'

/**
 * The one query client, shared by the app and the router's role guard. The template's defaults: a result is fresh for 30
 * seconds, so moving between pages does not refetch at once, and a failed query is tried once more (a dropped connection)
 * before the page shows the error.
 */
export const queryClient = new QueryClient({ defaultOptions: { queries: { staleTime: 30_000, retry: 1 } } })
