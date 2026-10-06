import { useCallback, useEffect, useState } from 'react'

export interface Route {
  /** The receipt being shown, or null for the start page and the splits page. */
  receiptId: string | null
  /** The receipts being split, one or several together, or null unless that is the page shown. */
  splitIds: string[] | null
  /** The receipt manager, which lists every receipt. */
  manage: boolean
}

interface HistoryState {
  previous: string
}

const receiptPath = /^\/receipts\/([^/]+)\/?$/
const splitsPath = /^\/splits\/?$/
const managePath = /^\/receipts\/?$/

const home: Route = { receiptId: null, splitIds: null, manage: false }

function readRoute(): Route {
  const { pathname, search } = window.location
  if (splitsPath.test(pathname)) {
    const ids = (new URLSearchParams(search).get('receipts') ?? '').split(',').filter((id) => id !== '')
    return ids.length > 0 ? { ...home, splitIds: ids } : home
  }
  if (managePath.test(pathname)) {
    return { ...home, manage: true }
  }
  const match = receiptPath.exec(pathname)
  return match ? { ...home, receiptId: match[1] } : home
}

function pathFor({ receiptId, splitIds, manage }: Route): string {
  if (manage) {
    return '/receipts'
  }
  if (splitIds) {
    return `/splits?receipts=${splitIds.map(encodeURIComponent).join(',')}`
  }
  return receiptId ? `/receipts/${receiptId}` : '/'
}

/**
 * The page being shown, kept in the URL as /receipts/:id, /receipts or /splits?receipts=a,b so links, reloads and
 * back/forward work. The backend serves index.html for these paths.
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
      // Remember where we came from, so goBack can return there instead of stacking another entry.
      window.history.pushState({ previous } satisfies HistoryState, '', path)
    }
    setRoute(next)
  }, [])

  /** Shows a receipt, or the start page for null. */
  const navigate = useCallback((receiptId: string | null) => go({ ...home, receiptId }), [go])

  /** Splits one receipt, or several together. */
  const navigateSplits = useCallback((ids: string[]) => go({ ...home, splitIds: ids }), [go])

  /** Shows the receipt manager. */
  const navigateManage = useCallback(() => go({ ...home, manage: true }), [go])

  /**
   * Goes back to a receipt, or the start page for null: a real history step when that is where the user came from,
   * otherwise (opened by a link or a reload) it replaces the current entry, so the browser's back button doesn't
   * return here either way.
   */
  const goBack = useCallback((receiptId: string | null) => {
    const next: Route = { ...home, receiptId }
    const path = pathFor(next)
    if ((window.history.state as HistoryState | null)?.previous === path) {
      window.history.back()
      return
    }
    window.history.replaceState(null, '', path)
    setRoute(next)
  }, [])

  return [route, navigate, goBack, navigateSplits, navigateManage] as const
}
