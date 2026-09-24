import { useEffect, useMemo, useState } from 'react'
import { Alert, Button, Card, Grid, Group, Stack, Text, Title } from '@mantine/core'
import { api, errorMessage, type ReceiptDetail } from '../api.ts'
import { formatMoney } from '../format.ts'
import { splitLines, summarize, summaryText } from '../splits.ts'
import { usePageTitle } from '../usePageTitle.ts'
import { useSplitState } from '../useSplitState.ts'
import { SplitPeople } from './SplitPeople.tsx'
import { SplitSummary } from './SplitSummary.tsx'
import { SplitTable } from './SplitTable.tsx'
import { SplitText } from './SplitText.tsx'

interface SplitsPageProps {
  id: string
  onBack: () => void
}

export function SplitsPage({ id, onBack }: SplitsPageProps) {
  const [receipt, setReceipt] = useState<ReceiptDetail | null>(null)
  const [error, setError] = useState<string | null>(null)
  usePageTitle(receipt ? `Split ${receipt.storeName ?? 'Unknown store'}` : 'Split')

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
        <Group gap="md" align="center">
          <Button variant="default" onClick={onBack} style={{ flexShrink: 0 }}>
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
      {receipt?.status === 'Completed' && <SplitBoard receipt={receipt} />}
    </Stack>
  )
}

function SplitBoard({ receipt }: { receipt: ReceiptDetail }) {
  const lines = useMemo(() => splitLines(receipt), [receipt])
  const [state, actions] = useSplitState()
  const summary = summarize(lines, state, receipt)

  return (
    <>
      <SplitPeople state={state} actions={actions} />
      <Grid gap="md" align="flex-start">
        <Grid.Col span={{ base: 12, lg: 8 }}>
          <SplitTable lines={lines} state={state} actions={actions} />
        </Grid.Col>
        <Grid.Col span={{ base: 12, lg: 4 }}>
          <Stack gap="md">
            <SplitSummary summary={summary} />
            <SplitText text={summaryText(summary)} />
          </Stack>
        </Grid.Col>
      </Grid>
    </>
  )
}
