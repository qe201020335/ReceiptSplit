import { Badge } from '@mantine/core'
import type { ReceiptStatus } from '../api.ts'
import { statusLabels } from '../format.ts'

const colors: Record<ReceiptStatus, string> = {
  Queued: 'gray',
  Processing: 'blue',
  Completed: 'green',
  NeedsReview: 'yellow',
  Failed: 'red',
}

export function StatusBadge({ status }: { status: ReceiptStatus }) {
  return (
    <Badge color={colors[status]} variant="light" size="sm">
      {statusLabels[status]}
    </Badge>
  )
}
