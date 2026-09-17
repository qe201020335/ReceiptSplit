import { useCallback, useEffect, useState } from 'react'

export type View = 'detail' | 'splits'

export interface Route {
  /** The receipt being shown, or null for the start page. */
  receiptId: string | null
  view: View
}

interface HistoryState {
  previous: string
}

const receiptPath = /^\/receipts\/([^/]+)(\/splits)?\/?$/

function readRoute(): Route {
  const match = receiptPath.exec(window.location.pathname)
  return match ? { receiptId: match[1], view: match[2] ? 'splits' : 'detail' } : { receiptId: null, view: 'detail' }
}

function pathFor({ receiptId, view }: Route): string {
  if (!receiptId) {
    return '/'
  }
  return view === 'splits' ? `/receipts/${receiptId}/splits` : `/receipts/${receiptId}`
}

/**
 * The page being shown, kept in the URL as /receipts/:id or /receipts/:id/splits so links, reloads and
 * back/forward work. The backend serves index.html for these paths.
 */
export function useRoute() {
  const [route, setRoute] = useState(readRoute)

  useEffect(() => {
    const onPopState = () => setRoute(readRoute())
    window.addEventListener('popstate', onPopState)
    return () => window.removeEventListener('popstate', onPopState)
  }, [])

  const navigate = useCallback((receiptId: string | null, view: View = 'detail') => {
    const next: Route = { receiptId, view: receiptId ? view : 'detail' }
    const path = pathFor(next)
    if (window.location.pathname !== path) {
      // Remember where we came from, so goBack can return there instead of stacking another entry.
      window.history.pushState({ previous: window.location.pathname } satisfies HistoryState, '', path)
    }
    setRoute(next)
  }, [])

  /**
   * Goes back to the given page: a real history step when that is where the user came from, otherwise (opened by a
   * link or a reload) it replaces the current entry, so the browser's back button doesn't return here either way.
   */
  const goBack = useCallback((receiptId: string | null, view: View = 'detail') => {
    const next: Route = { receiptId, view: receiptId ? view : 'detail' }
    const path = pathFor(next)
    if ((window.history.state as HistoryState | null)?.previous === path) {
      window.history.back()
      return
    }
    window.history.replaceState(null, '', path)
    setRoute(next)
  }, [])

  return [route, navigate, goBack] as const
}
