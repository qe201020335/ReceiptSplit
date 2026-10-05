// Names added on the splits page in this browser, most recent first, so the usual group is offered next time.

import { normalizePeople } from './splits.ts'

const storageKey = 'receiptsplit.recentNames'

export const maxRecentNames = 10

export function readRecentNames(): string[] {
  try {
    const stored: unknown = JSON.parse(window.localStorage.getItem(storageKey) ?? '[]')
    if (!Array.isArray(stored)) {
      return []
    }
    const names = stored.filter((name): name is string => typeof name === 'string')
    return normalizePeople(names).slice(0, maxRecentNames)
  } catch {
    return []
  }
}

/** Puts the added names first, spelled as just entered, and drops whatever falls past the cap. */
export function withRecentNames(saved: string[], added: string[]): string[] {
  return normalizePeople([...added, ...saved]).slice(0, maxRecentNames)
}

export function withoutRecentName(saved: string[], name: string): string[] {
  const key = name.toLocaleLowerCase()
  return saved.filter((entry) => entry.toLocaleLowerCase() !== key)
}

export function saveRecentNames(names: string[]): void {
  try {
    window.localStorage.setItem(storageKey, JSON.stringify(names))
  } catch {
    // Private windows and blocked site data are fine; nothing is offered next time.
  }
}
