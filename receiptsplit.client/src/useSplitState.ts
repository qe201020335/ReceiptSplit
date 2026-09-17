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
      changeShare: (position: number, person: string, delta: number) =>
        setState((current) => changeShare(current, position, person, delta)),
      addEveryone: (position: number) => setState((current) => addEveryone(current, position)),
      clearLine: (position: number) => setState((current) => clearLine(current, position)),
      setMode: (position: number, mode: SplitMode) => setState((current) => setMode(current, position, mode)),
      setAmount: (position: number, person: string, cents: number | null) =>
        setState((current) => setAmount(current, position, person, cents)),
    }),
    [],
  )

  return [state, actions] as const
}

export type SplitActions = ReturnType<typeof useSplitState>[1]
