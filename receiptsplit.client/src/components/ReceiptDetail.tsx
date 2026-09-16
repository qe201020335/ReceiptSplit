import { useEffect, useState } from 'react'
import {
  Accordion,
  Alert,
  Anchor,
  Button,
  Card,
  Code,
  Group,
  Image,
  List,
  NumberInput,
  Stack,
  Table,
  Text,
  Title,
} from '@mantine/core'
import { modals } from '@mantine/modals'
import { notifications } from '@mantine/notifications'
import { api, errorMessage, inProgress, type ReceiptDetail as Receipt } from '../api.ts'
import { formatDateTime, formatMoney, formatPercent, formatSeconds } from '../format.ts'
import { isValidTaxRate, maxTaxRatePercent, minTaxRatePercent } from '../taxRate.ts'
import { ReceiptEditor } from './ReceiptEditor.tsx'
import { StatusBadge } from './StatusBadge.tsx'

const pollIntervalMs = 2000

interface ReceiptDetailProps {
  id: string
  /** Called after an action that changes how the receipt appears in the list. */
  onChanged: () => void
  onDeleted: () => void
}

export function ReceiptDetail({ id, onChanged, onDeleted }: ReceiptDetailProps) {
  const [receipt, setReceipt] = useState<Receipt | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)
  // The rate being typed, or null when it is only being displayed.
  const [taxRateDraft, setTaxRateDraft] = useState<string | number | null>(null)
  const [editing, setEditing] = useState(false)
  // Bumping this reloads the receipt.
  const [version, setVersion] = useState(0)

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

  async function saveTaxRate(draft: string | number) {
    const rate = Number(draft)
    if (String(draft).trim() === '' || !isValidTaxRate(rate)) {
      return
    }

    setBusy(true)
    setError(null)
    try {
      const updated = await api.updateTaxRate(id, rate)
      setReceipt(updated)
      setTaxRateDraft(null)
      onChanged()
      notifications.show({ message: `Sales tax set to ${formatPercent(rate)}`, color: 'green' })
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
          <Text c="dimmed">Loading…</Text>
        )}
      </Card>
    )
  }

  const { checks, extraction } = receipt
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
                <Text size="sm">Sales tax {formatPercent(receipt.taxRatePercent)}</Text>
                <Anchor
                  component="button"
                  type="button"
                  size="sm"
                  onClick={() => setTaxRateDraft(receipt.taxRatePercent)}
                  disabled={busy || receipt.status === 'Processing'}
                >
                  Change
                </Anchor>
              </Group>
            ) : (
              <form
                onSubmit={(event) => {
                  event.preventDefault()
                  void saveTaxRate(taxRateDraft)
                }}
              >
                <Group gap="xs" align="flex-end">
                  <NumberInput
                    label="Sales tax"
                    size="xs"
                    w={110}
                    data-autofocus
                    autoFocus
                    suffix="%"
                    min={minTaxRatePercent}
                    max={maxTaxRatePercent}
                    decimalScale={3}
                    allowNegative={false}
                    value={taxRateDraft}
                    onChange={setTaxRateDraft}
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
              <Table.ScrollContainer minWidth={420}>
                <Table striped="odd" highlightOnHover verticalSpacing="xs" style={{ fontVariantNumeric: 'tabular-nums' }}>
                  <Table.Thead>
                    <Table.Tr>
                      <Table.Th w={40}>#</Table.Th>
                      <Table.Th>Item</Table.Th>
                      <Table.Th ta="right">Qty</Table.Th>
                      <Table.Th ta="right">Amount</Table.Th>
                      <Table.Th>Tax</Table.Th>
                    </Table.Tr>
                  </Table.Thead>
                  <Table.Tbody>
                    {receipt.lines.map((line) => (
                      <Table.Tr key={line.position}>
                        <Table.Td c="dimmed">{line.position + 1}</Table.Td>
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
                        <Table.Td style={{ whiteSpace: 'nowrap' }}>
                          {line.taxCode}
                          {line.isTaxed && (
                            <Text span c="green" title={`Taxed at ${formatPercent(receipt.taxRatePercent)}`}>
                              {' '}
                              ✓
                            </Text>
                          )}
                        </Table.Td>
                      </Table.Tr>
                    ))}
                  </Table.Tbody>
                  <Table.Tfoot>
                    <Table.Tr>
                      <Table.Th colSpan={3} ta="right">
                        Subtotal
                      </Table.Th>
                      <Table.Td ta="right">{formatMoney(receipt.subtotal)}</Table.Td>
                      <Table.Td />
                    </Table.Tr>
                    {checks && (
                      <Table.Tr>
                        <Table.Th colSpan={3} ta="right">
                          Taxed items
                        </Table.Th>
                        <Table.Td ta="right">{formatMoney(checks.taxedSum)}</Table.Td>
                        <Table.Td />
                      </Table.Tr>
                    )}
                    <Table.Tr>
                      <Table.Th colSpan={3} ta="right">
                        Tax
                      </Table.Th>
                      <Table.Td ta="right">{formatMoney(receipt.tax)}</Table.Td>
                      <Table.Td />
                    </Table.Tr>
                    <Table.Tr>
                      <Table.Th colSpan={3} ta="right">
                        Total
                      </Table.Th>
                      <Table.Td ta="right" fw={700}>
                        {formatMoney(receipt.total)}
                      </Table.Td>
                      <Table.Td />
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
                  {checks.totalMatches ? '✓ Subtotal + tax equals the total' : "✗ Subtotal + tax doesn't equal the total"}
                </List.Item>
                <List.Item c={checks.taxMatches ? 'green' : 'red'}>
                  {checks.taxMatches ? '✓' : '✗'} {formatPercent(receipt.taxRatePercent)} tax on{' '}
                  {formatMoney(checks.taxedSum)} of taxed items is {formatMoney(checks.expectedTax)}
                  {checks.taxMatches ? '' : `, but the receipt shows ${formatMoney(receipt.tax)}`}
                </List.Item>
              </List>
            )}

            <Group gap="xs">
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

        <Accordion variant="separated" chevronPosition="left" multiple>
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
