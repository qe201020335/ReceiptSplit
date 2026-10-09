import type { UserSummary } from './api.ts'

/** How people are named wherever owners show: the name, or else the email, as the account menu does. */
export function userLabel(user: UserSummary): string {
  return user.name ?? user.email ?? 'Unnamed user'
}

/**
 * Picks the owner filter's value from the URL's: 'none', a known user's id, or null for everyone, which is also what
 * an id shows when no one has it or the people couldn't be loaded.
 */
export function ownerFilter(owner: string | null, users: UserSummary[] | null): string | null {
  if (owner === null || owner === 'none') {
    return owner
  }
  return users?.some(({ id }) => id === owner) ? owner : null
}
