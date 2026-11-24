// Which links and pages to show. Cosmetic only: the API decides who may see and do what.

export const ADMIN_ROLE = 'admin'

/** The statistics page is for administrators. */
export function mayOpenStats(roles: readonly string[]): boolean {
  return roles.includes(ADMIN_ROLE)
}
