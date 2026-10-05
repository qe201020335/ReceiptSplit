import { useEffect, useMemo, useState } from 'react'
import { Alert, Button, Card, Grid, Group, Stack, Text, Title } from '@mantine/core'
import { api, errorMessage, type ReceiptDetail } from '../api.ts'
import { formatMoney } from '../format.ts'
import { formatCents, splitLines, summarize, summaryText, toCents } from '../splits.ts'
import { usePageTitle } from '../usePageTitle.ts'
import { useSplitState } from '../useSplitState.ts'
import { SplitPeople } from './SplitPeople.tsx'
import { SplitSummary } from './SplitSummary.tsx'
import { SplitTable } from './SplitTable.tsx'
import { SplitText } from './SplitText.tsx'

interface SplitsPageProps {
  /** One receipt, or several split together as one. */
  ids: string[]
  onBack: () => void
}

function storeName(receipt: ReceiptDetail): string {
  return receipt.storeName ?? 'Unknown store'
}

/** The receipts' totals added up, or a dash when any total is missing. */
function combinedTotal(receipts: ReceiptDetail[]): string {
  return receipts.some((receipt) => receipt.total == null)
    ? formatMoney(null)
    : formatCents(receipts.reduce((sum, receipt) => sum + toCents(receipt.total ?? 0), 0))
}

export function SplitsPage({ ids, onBack }: SplitsPageProps) {
  const [receipts, setReceipts] = useState<ReceiptDetail[] | null>(null)
  const [error, setError] = useState<string | null>(null)
  const several = ids.length > 1
  const title = several ? `Split ${ids.length} receipts` : receipts ? `Split ${storeName(receipts[0])}` : 'Split'
  usePageTitle(title)

  // The ids as a string, so a new array with the same receipts doesn't load them again.
  const idsKey = ids.join(',')
  useEffect(() => {
    let current = true
    Promise.all(idsKey.split(',').map((id) => api.getReceipt(id))).then(
      (loaded) => {
        if (current) {
          setReceipts(loaded)
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
  }, [idsKey])

  const notReady = receipts?.filter((receipt) => receipt.status !== 'Completed') ?? []
  const itemCount = receipts?.reduce((sum, receipt) => sum + receipt.lines.length, 0) ?? 0

  return (
    <Stack gap="md">
      <Card withBorder padding="md">
        <Group gap="md" align="center">
          <Button variant="default" onClick={onBack} style={{ flexShrink: 0 }}>
            {several ? '← Back to receipts' : '← Back to receipt'}
          </Button>
          {receipts && (
            <Stack gap={0} miw={0}>
              <Title order={2}>{title}</Title>
              <Text size="sm" c="dimmed">
                {several ? receipts.map(storeName).join(', ') : (receipts[0].purchaseDate ?? 'No purchase date')} ·{' '}
                {itemCount} items · total {combinedTotal(receipts)}
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
      {!receipts && !error && <Text c="dimmed">Loading…</Text>}
      {notReady.length > 0 &&
        (several ? (
          <Alert color="yellow" variant="light">
            {notReady.length === 1 ? (
              <>
                {storeName(notReady[0])} can't be split until it has been read and its lines agree with the printed
                totals. Go back to check or correct it, or leave it out of the split.
              </>
            ) : (
              <>
                {notReady.map(storeName).join(', ')} can't be split until they have been read and their lines agree
                with the printed totals. Go back to check or correct them, or leave them out of the split.
              </>
            )}
          </Alert>
        ) : (
          <Alert color="yellow" variant="light">
            This receipt can't be split until it has been read and its lines agree with the printed totals. Go back to
            check or correct it.
          </Alert>
        ))}
      {receipts && notReady.length === 0 && <SplitBoard receipts={receipts} />}
    </Stack>
  )
}

function SplitBoard({ receipts }: { receipts: ReceiptDetail[] }) {
  const lines = useMemo(() => receipts.flatMap(splitLines), [receipts])
  const [state, actions] = useSplitState()
  const summary = summarize(lines, state, receipts)

  return (
    <>
      <SplitPeople state={state} actions={actions} />
      <Grid gap="md" align="flex-start">
        <Grid.Col span={{ base: 12, lg: 8 }}>
          <SplitTable receipts={receipts} lines={lines} state={state} actions={actions} />
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
