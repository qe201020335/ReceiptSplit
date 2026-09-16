import { useCallback, useEffect, useState } from 'react'

const receiptPath = /^\/receipts\/([^/]+)\/?$/

function readSelectedId(): string | null {
  return receiptPath.exec(window.location.pathname)?.[1] ?? null
}

/**
 * The receipt shown in the detail view, kept in the URL as /receipts/:id so links, reloads and
 * back/forward work. The backend serves index.html for these paths.
 */
export function useSelectedReceipt() {
  const [selectedId, setSelectedId] = useState(readSelectedId)

  useEffect(() => {
    const onPopState = () => setSelectedId(readSelectedId())
    window.addEventListener('popstate', onPopState)
    return () => window.removeEventListener('popstate', onPopState)
  }, [])

  const select = useCallback((id: string | null) => {
    const path = id ? `/receipts/${id}` : '/'
    if (window.location.pathname !== path) {
      window.history.pushState(null, '', path)
    }
    setSelectedId(id)
  }, [])

  return [selectedId, select] as const
}
