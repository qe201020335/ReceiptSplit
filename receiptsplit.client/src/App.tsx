import { useCallback, useEffect, useState } from 'react'
import { api, errorMessage, inProgress, type ReceiptSummary } from './api.ts'
import { ReceiptDetail } from './components/ReceiptDetail.tsx'
import { ReceiptList } from './components/ReceiptList.tsx'
import { UploadForm } from './components/UploadForm.tsx'
import { useSelectedReceipt } from './useSelectedReceipt.ts'

const pollIntervalMs = 2000

function App() {
  const [receipts, setReceipts] = useState<ReceiptSummary[] | null>(null)
  const [listError, setListError] = useState<string | null>(null)
  // Bumping this reloads the list.
  const [listVersion, setListVersion] = useState(0)
  const [selectedId, selectReceipt] = useSelectedReceipt()

  const reloadList = useCallback(() => setListVersion((version) => version + 1), [])

  useEffect(() => {
    let current = true
    api.listReceipts().then(
      (list) => {
        if (current) {
          setReceipts(list)
          setListError(null)
        }
      },
      (error: unknown) => {
        if (current) {
          setListError(errorMessage(error))
        }
      },
    )
    return () => {
      current = false
    }
  }, [listVersion])

  // Keep statuses in the list current while any receipt is still being read.
  const anyInProgress = receipts?.some((receipt) => inProgress(receipt.status)) ?? false
  useEffect(() => {
    if (!anyInProgress) {
      return
    }
    const timer = setTimeout(reloadList, pollIntervalMs)
    return () => clearTimeout(timer)
  }, [receipts, anyInProgress, reloadList])

  return (
    <>
      <header className="app-header">
        <a
          href="/"
          className="brand"
          onClick={(event) => {
            event.preventDefault()
            selectReceipt(null)
          }}
        >
          ReceiptSplit
        </a>
      </header>
      <main className="layout">
        <aside className="sidebar">
          <UploadForm
            onUploaded={(receipt) => {
              reloadList()
              selectReceipt(receipt.id)
            }}
          />
          <ReceiptList receipts={receipts} error={listError} selectedId={selectedId} onSelect={selectReceipt} />
        </aside>
        <section className="content">
          {selectedId ? (
            <ReceiptDetail
              key={selectedId}
              id={selectedId}
              onChanged={reloadList}
              onDeleted={() => {
                selectReceipt(null)
                reloadList()
              }}
            />
          ) : (
            <p className="empty">Upload a receipt photo, or pick a receipt from the list.</p>
          )}
        </section>
      </main>
    </>
  )
}

export default App
