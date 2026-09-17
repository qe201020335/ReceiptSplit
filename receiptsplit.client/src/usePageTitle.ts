import { useEffect } from 'react'

const appName = 'ReceiptSplit'

/** Names the browser tab after the page, e.g. "COSTCO WHOLESALE – ReceiptSplit"; just the app name while null. */
export function usePageTitle(page: string | null) {
  useEffect(() => {
    document.title = page ? `${page} – ${appName}` : appName
    return () => {
      document.title = appName
    }
  }, [page])
}
