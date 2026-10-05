import { useCallback, useEffect, useState } from 'react'

export type View = 'detail' | 'splits'

export interface Route {
  /** The receipt being shown, or null for the start page and for a split of several receipts. */
  receiptId: string | null
  view: View
  /** The receipts being split together, or null unless that is the page shown. */
  splitIds: string[] | null
}

interface HistoryState {
  previous: string
}

const receiptPath = /^\/receipts\/([^/]+)(\/splits)?\/?$/
const splitsPath = /^\/splits\/?$/

const home: Route = { receiptId: null, view: 'detail', splitIds: null }

function readRoute(): Route {
  const { pathname, search } = window.location
  if (splitsPath.test(pathname)) {
    const ids = (new URLSearchParams(search).get('receipts') ?? '').split(',').filter((id) => id !== '')
    return ids.length > 0 ? { ...home, splitIds: ids } : home
  }
  const match = receiptPath.exec(pathname)
  return match ? { receiptId: match[1], view: match[2] ? 'splits' : 'detail', splitIds: null } : home
}

function pathFor({ receiptId, view, splitIds }: Route): string {
  if (splitIds) {
    return `/splits?receipts=${splitIds.map(encodeURIComponent).join(',')}`
  }
  if (!receiptId) {
    return '/'
  }
  return view === 'splits' ? `/receipts/${receiptId}/splits` : `/receipts/${receiptId}`
}

function currentPath(): string {
  return window.location.pathname + window.location.search
}

function receiptRoute(receiptId: string | null, view: View): Route {
  return { receiptId, view: receiptId ? view : 'detail', splitIds: null }
}

/**
 * The page being shown, kept in the URL as /receipts/:id, /receipts/:id/splits or /splits?receipts=a,b so links,
 * reloads and back/forward work. The backend serves index.html for these paths.
 */
export function useRoute() {
  const [route, setRoute] = useState(readRoute)

  useEffect(() => {
    const onPopState = () => setRoute(readRoute())
    window.addEventListener('popstate', onPopState)
    return () => window.removeEventListener('popstate', onPopState)
  }, [])

  const go = useCallback((next: Route) => {
    const path = pathFor(next)
    const previous = currentPath()
    if (previous !== path) {
      // Remember where we came from, so goBack can return there instead of stacking another entry.
      window.history.pushState({ previous } satisfies HistoryState, '', path)
    }
    setRoute(next)
  }, [])

  const navigate = useCallback(
    (receiptId: string | null, view: View = 'detail') => go(receiptRoute(receiptId, view)),
    [go],
  )

  /** Splits the receipts together; a single receipt gets its own splits page. */
  const navigateSplits = useCallback(
    (ids: string[]) => go(ids.length === 1 ? receiptRoute(ids[0], 'splits') : { ...home, splitIds: ids }),
    [go],
  )

  /**
   * Goes back to the given page: a real history step when that is where the user came from, otherwise (opened by a
   * link or a reload) it replaces the current entry, so the browser's back button doesn't return here either way.
   */
  const goBack = useCallback((receiptId: string | null, view: View = 'detail') => {
    const next = receiptRoute(receiptId, view)
    const path = pathFor(next)
    if ((window.history.state as HistoryState | null)?.previous === path) {
      window.history.back()
      return
    }
    window.history.replaceState(null, '', path)
    setRoute(next)
  }, [])

  return [route, navigate, goBack, navigateSplits] as const
}
