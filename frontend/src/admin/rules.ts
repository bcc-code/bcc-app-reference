// Which links and pages to show. Cosmetic only: the API decides who may see and do what.

export const ADMIN_ROLE = 'admin'

/** The statistics page is for administrators. */
export function mayOpenStats(roles: readonly string[]): boolean {
  return roles.includes(ADMIN_ROLE)
}

/** The message key for a provider key of the people list; a provider this page does not know shows as "other". */
export function providerLabelKey(provider: string): string {
  switch (provider) {
    case 'bcc-signon':
      return 'admin.people.provider.bccSignon'
    case 'entra':
      return 'admin.people.provider.entra'
    default:
      return 'admin.people.provider.other'
  }
}
