import { useEffect, useState } from 'react'
import { Alert, Button, Card, Group, Stack, Text, Title } from '@mantine/core'
import { api, errorMessage, type ReceiptDetail } from '../api.ts'
import { formatMoney } from '../format.ts'

interface SplitsPageProps {
  id: string
  onBack: () => void
}

export function SplitsPage({ id, onBack }: SplitsPageProps) {
  const [receipt, setReceipt] = useState<ReceiptDetail | null>(null)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    let current = true
    api.getReceipt(id).then(
      (loaded) => {
        if (current) {
          setReceipt(loaded)
          setError(null)
        }
      },
      (e: unknown) => {
        if (current) {
          setError(errorMessage(e))
        }
      },
    )
    return () => {
      current = false
    }
  }, [id])

  return (
    <Stack gap="md">
      <Card withBorder padding="md">
        <Group gap="md" wrap="nowrap" align="center">
          <Button variant="default" onClick={onBack}>
            ← Back to receipt
          </Button>
          {receipt && (
            <Stack gap={0} miw={0}>
              <Title order={2}>Split {receipt.storeName ?? 'Unknown store'}</Title>
              <Text size="sm" c="dimmed">
                {receipt.purchaseDate ?? 'No purchase date'} · {receipt.lines.length} items · total{' '}
                {formatMoney(receipt.total)}
              </Text>
            </Stack>
          )}
        </Group>
      </Card>

      {error && (
        <Alert color="red" variant="light">
          {error}
        </Alert>
      )}
      {!receipt && !error && <Text c="dimmed">Loading…</Text>}
      {receipt && receipt.status !== 'Completed' && (
        <Alert color="yellow" variant="light">
          This receipt can't be split until it has been read and its lines agree with the printed totals. Go back to
          check or correct it.
        </Alert>
      )}
    </Stack>
  )
}
