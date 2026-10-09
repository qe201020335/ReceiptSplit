import { useEffect, useState } from 'react'
import {
  Accordion,
  Alert,
  Anchor,
  Button,
  Card,
  Checkbox,
  Code,
  Group,
  Image,
  List,
  NumberInput,
  Stack,
  Table,
  Text,
  Title,
  Tooltip,
} from '@mantine/core'
import { useMediaQuery } from '@mantine/hooks'
import { modals } from '@mantine/modals'
import { notifications } from '@mantine/notifications'
import { api, errorMessage, inProgress, type ReceiptDetail as Receipt } from '../api.ts'
import { formatDateTime, formatMoney, formatPercent, formatSeconds } from '../format.ts'
import { isValidTaxRate, maxTaxRatePercent, minTaxRatePercent } from '../taxRate.ts'
import { usePageTitle } from '../usePageTitle.ts'
import { Bar, Loading } from './Placeholder.tsx'
import { ReceiptEditor } from './ReceiptEditor.tsx'
import { StatusBadge } from './StatusBadge.tsx'

const pollIntervalMs = 2000

interface ReceiptDetailProps {
  id: string
  /** Called after an action that changes how the receipt appears in the list. */
  onChanged: () => void
  onDeleted: () => void
  /** Opens the splits page; only offered once the receipt passes its checks. */
  onSplit: () => void
}

export function ReceiptDetail({ id, onChanged, onDeleted, onSplit }: ReceiptDetailProps) {
  const [receipt, setReceipt] = useState<Receipt | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)
  // The rate being typed, or null when it is only being displayed.
  const [taxRateDraft, setTaxRateDraft] = useState<string | number | null>(null)
  const [taxIncludedDraft, setTaxIncludedDraft] = useState(false)
  const [editing, setEditing] = useState(false)
  // Bumping this reloads the receipt.
  const [version, setVersion] = useState(0)
  // On a phone the line numbers go, so the amounts fit without scrolling the table sideways.
  usePageTitle(receipt ? (receipt.storeName ?? 'Unknown store') : null)
  const compact = useMediaQuery('(max-width: 36em)', undefined, { getInitialValueInEffect: false })

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
  }, [id, version])

  // While the model is reading the photo, reload a moment after each response.
  const waiting = receipt !== null && inProgress(receipt.status)
  useEffect(() => {
    if (!waiting) {
      return
    }
    const timer = setTimeout(() => setVersion((v) => v + 1), pollIntervalMs)
    return () => clearTimeout(timer)
  }, [receipt, waiting])

  async function saveTaxRate(draft: string | number, taxIncluded: boolean) {
    const rate = Number(draft)
    if (String(draft).trim() === '' || !isValidTaxRate(rate)) {
      return
    }

    setBusy(true)
    setError(null)
    try {
      const updated = await api.updateTaxRate(id, rate, taxIncluded)
      setReceipt(updated)
      setTaxRateDraft(null)
      onChanged()
      notifications.show({
        message: taxIncluded ? 'Prices include tax' : `Sales tax set to ${formatPercent(rate)}`,
        color: 'green',
      })
    } catch (e) {
      notifications.show({ title: "Couldn't change the tax rate", message: errorMessage(e), color: 'red' })
    } finally {
      setBusy(false)
    }
  }

  function confirmRerun() {
    if (receipt?.editedAt == null) {
      void rerun()
      return
    }

    modals.openConfirmModal({
      title: 'Read the photo again?',
      centered: true,
      children: <Text size="sm">This replaces the corrections you made by hand with whatever the model reads.</Text>,
      labels: { confirm: 'Read again', cancel: 'Keep corrections' },
      onConfirm: () => void rerun(),
    })
  }

  async function rerun() {
    setBusy(true)
    try {
      await api.rerunExtraction(id)
      setVersion((v) => v + 1)
      onChanged()
    } catch (e) {
      notifications.show({ title: "Couldn't start extraction", message: errorMessage(e), color: 'red' })
    } finally {
      setBusy(false)
    }
  }

  function confirmRemove() {
    modals.openConfirmModal({
      title: 'Delete this receipt?',
      centered: true,
      children: <Text size="sm">The receipt and its photo are deleted for good.</Text>,
      labels: { confirm: 'Delete', cancel: 'Cancel' },
      confirmProps: { color: 'red' },
      onConfirm: () => void remove(),
    })
  }

  async function remove() {
    setBusy(true)
    try {
      await api.deleteReceipt(id)
      onDeleted()
      notifications.show({ message: 'Receipt deleted', color: 'green' })
    } catch (e) {
      notifications.show({ title: "Couldn't delete the receipt", message: errorMessage(e), color: 'red' })
      setBusy(false)
    }
  }

  if (!receipt) {
    return (
      <Card withBorder padding="md">
        {error ? (
          <Alert color="red" variant="light">
            {error}
          </Alert>
        ) : (
          <ReceiptPlaceholder />
        )}
      </Card>
    )
  }

  const { checks, extraction } = receipt
  const canSplit = receipt.status === 'Completed' && !busy
  // Tax already inside the prices is ignored, so nothing about it is shown.
  const showTax = !receipt.taxIncluded
  const taxRateValid = taxRateDraft !== null && String(taxRateDraft).trim() !== '' && isValidTaxRate(Number(taxRateDraft))

  return (
    <Card withBorder padding="md" component="article">
      <Stack gap="md">
        <Group justify="space-between" align="flex-start" wrap="nowrap">
          <Stack gap={4}>
            <Title order={2}>{receipt.storeName ?? 'Unknown store'}</Title>
            <Text size="sm" c="dimmed" style={{ overflowWrap: 'anywhere' }}>
              {receipt.purchaseDate ?? 'No purchase date'} · uploaded {formatDateTime(receipt.createdAt)} ·{' '}
              {receipt.originalFileName}
              {receipt.editedAt && ` · corrected by hand ${formatDateTime(receipt.editedAt)}`}
            </Text>
            {taxRateDraft === null ? (
              <Group gap="xs">
                <Text size="sm">
                  {receipt.taxIncluded ? 'Prices include tax' : `Sales tax ${formatPercent(receipt.taxRatePercent)}`}
                </Text>
                <Anchor
                  component="button"
                  type="button"
                  size="sm"
                  onClick={() => {
                    setTaxRateDraft(receipt.taxRatePercent)
                    setTaxIncludedDraft(receipt.taxIncluded)
                  }}
                  disabled={busy || receipt.status === 'Processing'}
                >
                  Change
                </Anchor>
              </Group>
            ) : (
              <form
                onSubmit={(event) => {
                  event.preventDefault()
                  void saveTaxRate(taxRateDraft, taxIncludedDraft)
                }}
              >
                <Group gap="xs" align="flex-end">
                  <NumberInput
                    label="Sales tax"
                    size="xs"
                    w={110}
                    autoFocus
                    suffix="%"
                    min={minTaxRatePercent}
                    max={maxTaxRatePercent}
                    decimalScale={3}
                    allowNegative={false}
                    disabled={taxIncludedDraft}
                    value={taxRateDraft}
                    onChange={setTaxRateDraft}
                  />
                  <Checkbox
                    label="Prices include tax"
                    size="xs"
                    mb={6}
                    checked={taxIncludedDraft}
                    onChange={(event) => setTaxIncludedDraft(event.currentTarget.checked)}
                  />
                  <Button type="submit" size="xs" variant="default" disabled={busy || !taxRateValid}>
                    Save
                  </Button>
                  <Button size="xs" variant="subtle" onClick={() => setTaxRateDraft(null)} disabled={busy}>
                    Cancel
                  </Button>
                </Group>
              </form>
            )}
          </Stack>
          <StatusBadge status={receipt.status} />
        </Group>

        {error && (
          <Alert color="red" variant="light">
            {error}
          </Alert>
        )}
        {waiting && (
          <Alert color="blue" variant="light">
            Reading the receipt. This usually takes about 20 seconds, or up to a minute if the model has to load first.
          </Alert>
        )}
        {receipt.error && (
          <Alert color="red" variant="light">
            {receipt.error}
          </Alert>
        )}
        {receipt.status === 'NeedsReview' && !receipt.error && (
          <Alert color="yellow" variant="light">
            The lines don't agree with the receipt's printed totals. Check them against the photo, and check that the sales
            tax rate is right for where you shopped.
          </Alert>
        )}

        {editing ? (
          <ReceiptEditor
            receipt={receipt}
            onSaved={(corrected) => {
              setReceipt(corrected)
              setEditing(false)
              onChanged()
            }}
            onCancel={() => setEditing(false)}
          />
        ) : (
          <>
            {receipt.lines.length > 0 && (
              <Table.ScrollContainer minWidth={compact ? 0 : 420}>
                <Table striped="odd" highlightOnHover verticalSpacing="xs" style={{ fontVariantNumeric: 'tabular-nums' }}>
                  <Table.Thead>
                    <Table.Tr>
                      {!compact && <Table.Th w={40}>#</Table.Th>}
                      <Table.Th>Item</Table.Th>
                      <Table.Th ta="right">Qty</Table.Th>
                      <Table.Th ta="right">Amount</Table.Th>
                      {showTax && <Table.Th>Tax</Table.Th>}
                    </Table.Tr>
                  </Table.Thead>
                  <Table.Tbody>
                    {receipt.lines.map((line) => (
                      <Table.Tr key={line.position}>
                        {!compact && <Table.Td c="dimmed">{line.position + 1}</Table.Td>}
                        <Table.Td>
                          {line.name}
                          {line.code && (
                            <Text span c="dimmed" size="xs">
                              {' '}
                              {line.code}
                            </Text>
                          )}
                          {line.discount !== 0 && (
                            <Text size="xs" c="dimmed">
                              was {formatMoney(line.amount - line.discount)}, promotion {formatMoney(line.discount)}
                            </Text>
                          )}
                        </Table.Td>
                        <Table.Td ta="right">{line.quantity}</Table.Td>
                        <Table.Td ta="right">{formatMoney(line.amount)}</Table.Td>
                        {showTax && (
                          <Table.Td style={{ whiteSpace: 'nowrap' }}>
                            {line.taxCode}
                            {line.isTaxed && (
                              <Text span c="green" title={`Taxed at ${formatPercent(receipt.taxRatePercent)}`}>
                                {' '}
                                ✓
                              </Text>
                            )}
                          </Table.Td>
                        )}
                      </Table.Tr>
                    ))}
                  </Table.Tbody>
                  <Table.Tfoot>
                    <Table.Tr>
                      <Table.Th colSpan={compact ? 2 : 3} ta="right">
                        Subtotal
                      </Table.Th>
                      <Table.Td ta="right">{formatMoney(receipt.subtotal)}</Table.Td>
                      {showTax && <Table.Td />}
                    </Table.Tr>
                    {checks && receipt.discountPercent !== 0 && (
                      <Table.Tr>
                        <Table.Th colSpan={compact ? 2 : 3} ta="right">
                          Discount ({formatPercent(receipt.discountPercent)} off)
                        </Table.Th>
                        <Table.Td ta="right">{formatMoney(checks.discount)}</Table.Td>
                        {showTax && <Table.Td />}
                      </Table.Tr>
                    )}
                    {checks && showTax && (
                      <Table.Tr>
                        <Table.Th colSpan={compact ? 2 : 3} ta="right">
                          Taxed items
                        </Table.Th>
                        <Table.Td ta="right">{formatMoney(checks.taxedSum)}</Table.Td>
                        <Table.Td />
                      </Table.Tr>
                    )}
                    {showTax && (
                      <Table.Tr>
                        <Table.Th colSpan={compact ? 2 : 3} ta="right">
                          Tax
                        </Table.Th>
                        <Table.Td ta="right">{formatMoney(receipt.tax)}</Table.Td>
                        <Table.Td />
                      </Table.Tr>
                    )}
                    <Table.Tr>
                      <Table.Th colSpan={compact ? 2 : 3} ta="right">
                        Total
                      </Table.Th>
                      <Table.Td ta="right" fw={700}>
                        {formatMoney(receipt.total)}
                      </Table.Td>
                      {showTax && <Table.Td />}
                    </Table.Tr>
                  </Table.Tfoot>
                </Table>
              </Table.ScrollContainer>
            )}

            {checks && (
              <List spacing={2} size="sm" listStyleType="none">
                <List.Item c={checks.linesMatchSubtotal ? 'green' : 'red'}>
                  {checks.linesMatchSubtotal
                    ? `✓ Lines add up to the subtotal (${formatMoney(checks.linesSum)})`
                    : `✗ Lines add up to ${formatMoney(checks.linesSum)}, but the subtotal is ${formatMoney(receipt.subtotal)}`}
                </List.Item>
                <List.Item c={checks.totalMatches ? 'green' : 'red'}>
                  {checks.totalMatches ? '✓' : '✗'} Subtotal {receipt.discountPercent !== 0 && '− discount '}
                  {showTax && '+ tax '}
                  {checks.totalMatches ? 'equals' : "doesn't equal"} the total
                </List.Item>
                {showTax && (
                  <List.Item c={checks.taxMatches ? 'green' : 'red'}>
                    {checks.taxMatches ? '✓' : '✗'} {formatPercent(receipt.taxRatePercent)} tax on{' '}
                    {formatMoney(checks.taxedSum)} of taxed items is {formatMoney(checks.expectedTax)}
                    {checks.taxMatches ? '' : `, but the receipt shows ${formatMoney(receipt.tax)}`}
                  </List.Item>
                )}
              </List>
            )}

            <Group gap="xs">
              <Tooltip label="Available once the receipt is read and its totals check out" disabled={canSplit}>
                {/* A disabled button fires no pointer events, so the tooltip hangs off a wrapper. */}
                <span>
                  <Button onClick={onSplit} disabled={!canSplit}>
                    Split
                  </Button>
                </span>
              </Tooltip>
              <Button variant="default" onClick={() => setEditing(true)} disabled={busy || waiting}>
                Edit lines
              </Button>
              <Button variant="default" onClick={confirmRerun} disabled={busy || waiting}>
                Re-run extraction
              </Button>
              <Button variant="default" c="red" onClick={confirmRemove} disabled={busy || receipt.status === 'Processing'}>
                Delete
              </Button>
            </Group>
          </>
        )}

        <Accordion variant="contained" chevronPosition="left" multiple>
          <Accordion.Item value="photo">
            <Accordion.Control>Original photo</Accordion.Control>
            <Accordion.Panel>
              <Anchor href={api.imageUrl(receipt.id)} target="_blank" rel="noreferrer">
                <Image
                  src={api.imageUrl(receipt.id)}
                  alt={`Uploaded photo ${receipt.originalFileName}`}
                  loading="lazy"
                  radius="sm"
                  mah="80vh"
                  maw="100%"
                  w="auto"
                  fit="contain"
                />
              </Anchor>
            </Accordion.Panel>
          </Accordion.Item>
          {extraction && (
            <Accordion.Item value="extraction">
              <Accordion.Control>Extraction details</Accordion.Control>
              <Accordion.Panel>
                <Stack gap="xs">
                  <Text size="sm" c="dimmed">
                    {extraction.model ?? 'Unknown model'}
                    {extraction.durationMs != null && ` · ${formatSeconds(extraction.durationMs)}`}
                    {extraction.promptTokens != null &&
                      ` · ${extraction.promptTokens} prompt + ${extraction.completionTokens} output tokens`}
                    {extraction.sentImageWidth != null &&
                      ` · image sent at ${extraction.sentImageWidth}×${extraction.sentImageHeight}`}
                  </Text>
                  {extraction.modelOutput && (
                    <Code block mah="20rem" style={{ overflow: 'auto', whiteSpace: 'pre-wrap', overflowWrap: 'anywhere' }}>
                      {extraction.modelOutput}
                    </Code>
                  )}
                </Stack>
              </Accordion.Panel>
            </Accordion.Item>
          )}
        </Accordion>
      </Stack>
    </Card>
  )
}

/** Line widths for the placeholder's lines, fixed so they differ but render the same every time. */
const placeholderLineWidths = ['52%', '38%', '61%', '45%', '33%']

/**
 * The receipt card's shape while it loads: the store name, the details and tax line under it with the status on the
 * right, then rows where the lines go. Line boxes use the real text's heights, so the header doesn't move.
 */
function ReceiptPlaceholder() {
  return (
    <Loading label="Loading the receipt">
      <Stack gap="md">
        <Group justify="space-between" align="flex-start" wrap="nowrap">
          <Stack gap={4} flex={1}>
            <Group h="calc(1.05rem * 1.3)">
              <Bar height={16} width="40%" />
            </Group>
            <Group h="calc(0.875rem * 1.45)">
              <Bar height={10} width="70%" />
            </Group>
            <Group h="calc(0.875rem * 1.45)">
              <Bar height={10} width="25%" />
            </Group>
          </Stack>
          <Bar height={18} width={78} radius="xl" />
        </Group>
        <Stack gap={0}>
          {placeholderLineWidths.map((width) => (
            <Group key={width} h={44} justify="space-between" wrap="nowrap" px="xs">
              <Bar height={12} width={width} />
              <Bar height={12} width={56} />
            </Group>
          ))}
        </Stack>
      </Stack>
    </Loading>
  )
}
