import { useState } from 'react'
import { readRecentNames, saveRecentNames, withoutRecentName, withRecentNames } from './recentNames.ts'

/** Names to offer on the splits page, saved to this browser on every change. */
export function useRecentNames() {
  const [names, setNames] = useState(readRecentNames)

  function update(next: string[]) {
    setNames(next)
    saveRecentNames(next)
  }

  const actions = {
    remember: (added: string[]) => update(withRecentNames(names, added)),
    forget: (name: string) => update(withoutRecentName(names, name)),
  }

  return [names, actions] as const
}
