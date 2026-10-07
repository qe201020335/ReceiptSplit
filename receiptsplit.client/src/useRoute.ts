import { useCallback, useEffect, useState } from 'react'

/** The page being shown; each has its own URL. */
export type Route =
  /** The start page, /, with a receipt beside the list at /?receipt={id}. */
  | { page: 'home'; receiptId: string | null }
  /** One receipt on its own page, /receipts/{id}. */
  | { page: 'receipt'; receiptId: string }
  /** The receipt manager, /receipts, which lists every receipt. */
  | { page: 'manage' }
  /** One receipt split, or several together, /splits?receipts=a,b. */
  | { page: 'splits'; ids: string[] }

interface HistoryState {
  previous: string
}

const receiptPath = /^\/receipts\/([^/]+)\/?$/
const managePath = /^\/receipts\/?$/
const splitsPath = /^\/splits\/?$/

export const home: Route = { page: 'home', receiptId: null }

function readRoute(): Route {
  const { pathname, search } = window.location
  const query = new URLSearchParams(search)
  if (splitsPath.test(pathname)) {
    const ids = (query.get('receipts') ?? '').split(',').filter((id) => id !== '')
    return ids.length > 0 ? { page: 'splits', ids } : home
  }
  if (managePath.test(pathname)) {
    return { page: 'manage' }
  }
  const match = receiptPath.exec(pathname)
  if (match) {
    return { page: 'receipt', receiptId: decodeURIComponent(match[1]) }
  }
  return { page: 'home', receiptId: query.get('receipt') || null }
}

export function pathFor(route: Route): string {
  switch (route.page) {
    case 'home':
      return route.receiptId ? `/?receipt=${encodeURIComponent(route.receiptId)}` : '/'
    case 'receipt':
      return `/receipts/${encodeURIComponent(route.receiptId)}`
    case 'manage':
      return '/receipts'
    case 'splits':
      return `/splits?receipts=${route.ids.map(encodeURIComponent).join(',')}`
  }
}

/**
 * The page being shown, kept in the URL so links, reloads and back/forward work. The backend serves index.html for
 * these paths.
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
    const previous = window.location.pathname + window.location.search
    if (previous !== path) {
      // Remember that this entry was reached from within the app, so goBack can step back to it.
      window.history.pushState({ previous } satisfies HistoryState, '', path)
    }
    setRoute(next)
  }, [])

  /**
   * Returns to the page the user came from with a real history step. A page opened by a link or a reload has nowhere
   * in the app to return to, so it is replaced by the fallback, and the browser's back button doesn't return here.
   */
  const goBack = useCallback((fallback: Route) => {
    if ((window.history.state as HistoryState | null)?.previous !== undefined) {
      window.history.back()
      return
    }
    window.history.replaceState(null, '', pathFor(fallback))
    setRoute(fallback)
  }, [])

  /** Shows another page in place of this one, keeping how the user got here so Back still returns there. */
  const replace = useCallback((next: Route) => {
    window.history.replaceState(window.history.state, '', pathFor(next))
    setRoute(next)
  }, [])

  return [route, go, goBack, replace] as const
}
