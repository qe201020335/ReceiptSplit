import type { ReceiptSummary } from '../api.ts'
import { formatDateTime, formatMoney } from '../format.ts'
import { StatusBadge } from './StatusBadge.tsx'

interface ReceiptListProps {
  receipts: ReceiptSummary[] | null
  error: string | null
  selectedId: string | null
  onSelect: (id: string) => void
}

export function ReceiptList({ receipts, error, selectedId, onSelect }: ReceiptListProps) {
  return (
    <nav className="card receipt-list" aria-label="Receipts">
      <h2>Receipts</h2>
      {error && <p className="error">{error}</p>}
      {receipts === null && !error && <p className="muted">Loading…</p>}
      {receipts?.length === 0 && <p className="muted">No receipts yet.</p>}
      <ul>
        {receipts?.map((receipt) => (
          <li key={receipt.id}>
            <button
              type="button"
              className={receipt.id === selectedId ? 'receipt-item selected' : 'receipt-item'}
              aria-current={receipt.id === selectedId ? 'page' : undefined}
              onClick={() => onSelect(receipt.id)}
            >
              <span className="receipt-item-title">{receipt.storeName ?? 'Unknown store'}</span>
              <span className="receipt-item-total">{formatMoney(receipt.total)}</span>
              <span className="muted">{receipt.purchaseDate ?? formatDateTime(receipt.createdAt)}</span>
              <StatusBadge status={receipt.status} />
            </button>
          </li>
        ))}
      </ul>
    </nav>
  )
}
