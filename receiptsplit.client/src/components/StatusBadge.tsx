import type { ReceiptStatus } from '../api.ts'
import { statusLabels } from '../format.ts'

export function StatusBadge({ status }: { status: ReceiptStatus }) {
  return <span className={`badge badge-${status.toLowerCase()}`}>{statusLabels[status]}</span>
}
