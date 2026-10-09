import type { UserSummary } from './api.ts'

/** How people are named wherever owners show: the name, or else the email, as the account menu does. */
export function userLabel(user: UserSummary): string {
  return user.name ?? user.email ?? 'Unnamed user'
}

/** Picks the owner filter's value from the URL's: 'none', a known user's id, or null for everyone. */
export function ownerFilter(owner: string | null, users: UserSummary[] | null): string | null {
  if (owner === null || owner === 'none') {
    return owner
  }
  // Until the people load, an id filters anyway; summaries carry owner ids, not names.
  return users === null || users.some(({ id }) => id === owner) ? owner : null
}
