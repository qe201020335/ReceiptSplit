import { useEffect, useMemo, useState } from 'react'
import { Alert, Box, Button, Card, Grid, Group, Stack, Text, Title } from '@mantine/core'
import { api, errorMessage, type ReceiptDetail } from '../api.ts'
import { formatMoney } from '../format.ts'
import { formatCents, splitLines, summarize, summaryText, toCents } from '../splits.ts'
import { usePageTitle } from '../usePageTitle.ts'
import { useSplitState } from '../useSplitState.ts'
import { Bar, Loading } from './Placeholder.tsx'
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

/** Line widths for the placeholder rows, fixed so they differ but render the same every time. */
const splitPlaceholderWidths = ['48%', '36%', '57%', '42%', '30%', '52%']

/** One line of small text, as the cards' hints and totals are. */
const smallLine = 'calc(0.875rem * 1.45)'

/** A card heading's line: the h2's size and line height from theme.ts. */
const headingLine = 'calc(1.05rem * 1.3)'

/**
 * SplitBoard's shape while the receipts load, in the same Grid: the People input, the lines table on the left, and
 * Who owes what and Summary text on the right (below it on smaller screens), so the cards stay where they'll be.
 */
function SplitBoardPlaceholder() {
  return (
    <Loading label="Loading the receipts to split">
      <Stack gap="md">
        <Card withBorder padding="md">
          <Stack gap={3}>
            <Group h={22}>
              <Bar height={12} width={56} />
            </Group>
            <Bar height={36} />
          </Stack>
        </Card>
        <Grid gap="md" align="flex-start">
          <Grid.Col span={{ base: 12, lg: 8 }}>
            <Card withBorder padding="md">
              <Stack gap={0} mb="xs">
                <Group h={smallLine}>
                  <Bar height={10} width="90%" />
                </Group>
                <Group h={smallLine}>
                  <Bar height={10} width="60%" />
                </Group>
              </Stack>
              {splitPlaceholderWidths.map((width) => (
                <Group key={width} h={44} justify="space-between" wrap="nowrap">
                  <Bar height={12} width={width} />
                  <Bar height={12} width={56} />
                </Group>
              ))}
            </Card>
          </Grid.Col>
          <Grid.Col span={{ base: 12, lg: 4 }}>
            <Stack gap="md">
              <Card withBorder padding="md">
                <Group h={headingLine} mb="xs">
                  <Bar height={16} width="55%" />
                </Group>
                <Group h={smallLine}>
                  <Bar height={10} width="70%" />
                </Group>
                <Group h={smallLine} mt="sm">
                  <Bar height={10} width="85%" />
                </Group>
              </Card>
              <Card withBorder padding="md">
                <Group h={30} justify="space-between" wrap="nowrap" mb="xs">
                  <Bar height={16} width="45%" />
                  <Bar height={30} width={120} />
                </Group>
                <Group h={smallLine}>
                  <Bar height={10} width="90%" />
                </Group>
                <Group h={smallLine}>
                  <Bar height={10} width="50%" />
                </Group>
              </Card>
            </Stack>
          </Grid.Col>
        </Grid>
      </Stack>
    </Loading>
  )
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
          {!receipts && !error && (
            // The title and summary lines, so the header keeps its height when they arrive.
            // On a phone the title and summary wrap below Back, as the loaded ones do, and several receipts' summary
            // takes two lines.
            <Box flex={{ base: '1 0 100%', xs: 1 }} miw={0}>
              <Loading label="Loading the split">
                <Stack gap={0}>
                  <Group h="calc(1.05rem * 1.3)">
                    <Bar height={16} width="45%" />
                  </Group>
                  <Group h="calc(0.875rem * 1.45)">
                    <Bar height={10} width="75%" />
                  </Group>
                  {several && (
                    <Group h="calc(0.875rem * 1.45)" hiddenFrom="xs">
                      <Bar height={10} width="40%" />
                    </Group>
                  )}
                </Stack>
              </Loading>
            </Box>
          )}
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
      {!receipts && !error && <SplitBoardPlaceholder />}
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
