import { Alert, Card, Group, NavLink, Stack, Text, Title } from '@mantine/core'
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
    <Card withBorder padding="md" component="nav" aria-label="Receipts">
      <Stack gap="sm">
        <Title order={2}>Receipts</Title>
        {error && (
          <Alert color="red" variant="light">
            {error}
          </Alert>
        )}
        {receipts === null && !error && <Text c="dimmed">Loading…</Text>}
        {receipts?.length === 0 && <Text c="dimmed">No receipts yet.</Text>}
        <Stack gap={2}>
          {receipts?.map((receipt) => (
            <NavLink
              key={receipt.id}
              component="button"
              type="button"
              active={receipt.id === selectedId}
              onClick={() => onSelect(receipt.id)}
              label={
                <Group justify="space-between" wrap="nowrap" gap="xs">
                  <Text fw={500} truncate>
                    {receipt.storeName ?? 'Unknown store'}
                  </Text>
                  <Text fw={500}>{formatMoney(receipt.total)}</Text>
                </Group>
              }
              description={
                <Group justify="space-between" wrap="nowrap" gap="xs">
                  <span>{receipt.purchaseDate ?? formatDateTime(receipt.createdAt)}</span>
                  <StatusBadge status={receipt.status} />
                </Group>
              }
            />
          ))}
        </Stack>
      </Stack>
    </Card>
  )
}
