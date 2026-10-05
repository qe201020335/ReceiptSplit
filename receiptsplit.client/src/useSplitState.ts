import { useMemo, useState } from 'react'
import {
  addEveryone,
  changeShare,
  clearLine,
  emptySplitState,
  setAmount,
  setMode,
  setPeople,
  type SplitMode,
} from './splits.ts'

/** Who pays for what on the splits page. Kept in memory only: leaving or reloading the page starts over. */
export function useSplitState() {
  const [state, setState] = useState(emptySplitState)

  const actions = useMemo(
    () => ({
      setPeople: (names: string[]) => setState((current) => setPeople(current, names)),
      changeShare: (key: string, person: string, delta: number) =>
        setState((current) => changeShare(current, key, person, delta)),
      addEveryone: (key: string) => setState((current) => addEveryone(current, key)),
      clearLine: (key: string) => setState((current) => clearLine(current, key)),
      setMode: (key: string, mode: SplitMode) => setState((current) => setMode(current, key, mode)),
      setAmount: (key: string, person: string, cents: number | null) =>
        setState((current) => setAmount(current, key, person, cents)),
    }),
    [],
  )

  return [state, actions] as const
}

export type SplitActions = ReturnType<typeof useSplitState>[1]
