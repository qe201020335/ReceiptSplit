import { useState } from 'react'
import { Alert, Anchor, Button, Card, Checkbox, Group, Stack, Text, Title, UnstyledButton } from '@mantine/core'
import { modals } from '@mantine/modals'
import { notifications } from '@mantine/notifications'
import { api, errorMessage, type ReceiptSummary } from '../api.ts'
import { formatDateTime, formatMoney } from '../format.ts'
import { usePageTitle } from '../usePageTitle.ts'
import { pathFor } from '../useRoute.ts'
import { StatusBadge } from './StatusBadge.tsx'
import classes from './ReceiptManager.module.css'

interface ReceiptManagerProps {
  receipts: ReceiptSummary[] | null
  error: string | null
  /** Called after receipts were deleted, to reload the list. */
  onChanged: () => void
  onOpen: (id: string) => void
  onBack: () => void
}

interface Month {
  key: string
  label: string
  receipts: ReceiptSummary[]
}

function receiptCount(count: number): string {
  return `${count} receipt${count === 1 ? '' : 's'}`
}

/** Groups the newest-first list by the month each receipt was added, so the groups keep the list's order. */
function byMonth(receipts: ReceiptSummary[]): Month[] {
  const months: Month[] = []
  for (const receipt of receipts) {
    const added = new Date(receipt.createdAt)
    const key = `${added.getFullYear()}-${added.getMonth()}`
    if (months.at(-1)?.key !== key) {
      const label = added.toLocaleDateString(undefined, { month: 'long', year: 'numeric' })
      months.push({ key, label, receipts: [] })
    }
    months.at(-1)!.receipts.push(receipt)
  }
  return months
}

/** Every receipt, to select several and delete them; later also where receipts get managed in other ways. */
export function ReceiptManager({ receipts, error, onChanged, onOpen, onBack }: ReceiptManagerProps) {
  const [selected, setSelected] = useState<ReadonlySet<string>>(() => new Set())
  const [deleting, setDeleting] = useState(false)
  usePageTitle('Manage receipts')

  // In list order, and without receipts that were deleted since they were selected.
  const selectedReceipts = (receipts ?? []).filter((receipt) => selected.has(receipt.id))
  const selectedIds = selectedReceipts.map(({ id }) => id)
  // The server won't delete a receipt while the model is reading it, so Delete waits until it's read.
  const readingCount = selectedReceipts.filter((receipt) => receipt.status === 'Processing').length
  const months = byMonth(receipts ?? [])

  function toggle(ids: string[], on: boolean) {
    setSelected((current) => {
      const next = new Set(current)
      for (const id of ids) {
        if (on) {
          next.add(id)
        } else {
          next.delete(id)
        }
      }
      return next
    })
  }

  function confirmDelete() {
    if (readingCount > 0) {
      const which = readingCount === 1 ? '1 selected receipt is' : `${readingCount} selected receipts are`
      notifications.show({ message: `${which} being read and can't be deleted yet.` })
      return
    }

    const ids = selectedIds
    const count = receiptCount(ids.length)
    modals.openConfirmModal({
      title: `Delete ${count}?`,
      centered: true,
      children: (
        <Text size="sm">
          {ids.length === 1
            ? 'The receipt and its photo are deleted for good.'
            : 'The receipts and their photos are deleted for good.'}
        </Text>
      ),
      labels: { confirm: `Delete ${count}`, cancel: 'Cancel' },
      confirmProps: { color: 'red' },
      onConfirm: () => void remove(ids),
    })
  }

  async function remove(ids: string[]) {
    setDeleting(true)
    try {
      await api.deleteReceipts(ids)
      setSelected(new Set())
      notifications.show({ message: `${receiptCount(ids.length)} deleted`, color: 'green' })
    } catch (e) {
      // Nothing was deleted, and the message says which receipts stopped it. The selection stays for another try;
      // the reload drops receipts that are gone or being read from it.
      notifications.show({ title: "Couldn't delete the receipts", message: errorMessage(e), color: 'red' })
    } finally {
      setDeleting(false)
      onChanged()
    }
  }

  return (
    <Stack gap="md">
      <Card withBorder padding="md">
        <Group gap="md" wrap="nowrap">
          <Button variant="default" onClick={onBack} style={{ flexShrink: 0 }}>
            ← Back
          </Button>
          <Stack gap={0} miw={0}>
            <Title order={2}>Manage receipts</Title>
            {receipts && receipts.length > 0 && (
              <Text size="sm" c="dimmed">
                {receiptCount(receipts.length)}
              </Text>
            )}
          </Stack>
        </Group>
      </Card>
      {error && (
        <Alert color="red" variant="light">
          {error}
        </Alert>
      )}
      {receipts === null && !error && <Text c="dimmed">Loading…</Text>}
      {receipts?.length === 0 && (
        <Card withBorder padding="xl">
          <Text c="dimmed" ta="center">
            No receipts yet.{' '}
            <Anchor
              href="/"
              onClick={(event) => {
                event.preventDefault()
                onBack()
              }}
            >
              Upload a receipt photo
            </Anchor>
          </Text>
        </Card>
      )}
      {months.length > 0 && (
        <Card withBorder padding={0} className={classes.card}>
          {months.map((month) => {
            const ids = month.receipts.map(({ id }) => id)
            const checkedCount = ids.filter((id) => selected.has(id)).length
            return (
              <section key={month.key} aria-label={month.label}>
                <div className={classes.month}>
                  <Checkbox
                    label={month.label}
                    aria-label={`Select all from ${month.label}`}
                    checked={ids.length > 0 && checkedCount === ids.length}
                    indeterminate={checkedCount > 0 && checkedCount < ids.length}
                    disabled={ids.length === 0 || deleting}
                    onChange={() => toggle(ids, checkedCount < ids.length)}
                    classNames={{ label: classes.monthLabel }}
                  />
                  <Text size="sm" c="dimmed">
                    {receiptCount(month.receipts.length)}
                  </Text>
                </div>
                {month.receipts.map((receipt) => (
                  <ReceiptManagerRow
                    key={receipt.id}
                    receipt={receipt}
                    checked={selected.has(receipt.id)}
                    disabled={deleting}
                    onToggle={(on) => toggle([receipt.id], on)}
                    onOpen={() => onOpen(receipt.id)}
                  />
                ))}
              </section>
            )
          })}
          {selectedIds.length > 0 && (
            // The count is in the Delete button, which leaves the footer room for both buttons on a phone.
            <div className={classes.footer}>
              <Button variant="default" disabled={deleting} onClick={() => setSelected(new Set())}>
                Clear
              </Button>
              {/* Not disabled while a selected receipt is being read: it stays clickable, to say why it won't delete. */}
              <Button
                color="red"
                loading={deleting}
                data-disabled={readingCount > 0 || undefined}
                aria-disabled={readingCount > 0 || undefined}
                onClick={confirmDelete}
              >
                Delete {receiptCount(selectedIds.length)}
              </Button>
            </div>
          )}
        </Card>
      )}
    </Stack>
  )
}

interface ReceiptManagerRowProps {
  receipt: ReceiptSummary
  checked: boolean
  disabled: boolean
  onToggle: (on: boolean) => void
  onOpen: () => void
}

/**
 * One receipt: the row itself selects it, and Open sits beside it rather than inside, so the two never clash. Further
 * per-receipt details and actions belong here.
 */
function ReceiptManagerRow({ receipt, checked, disabled, onToggle, onOpen }: ReceiptManagerRowProps) {
  const date = receipt.purchaseDate ?? formatDateTime(receipt.createdAt)
  return (
    <div className={classes.row} data-checked={checked || undefined}>
      <UnstyledButton
        role="checkbox"
        aria-checked={checked}
        disabled={disabled}
        className={classes.toggle}
        onClick={() => onToggle(!checked)}
      >
        <Checkbox.Indicator checked={checked} disabled={disabled} />
        <Stack gap={2} miw={0} flex={1}>
          <Group justify="space-between" wrap="nowrap" gap="xs">
            <Text fw={500} truncate>
              {receipt.storeName ?? 'Unknown store'}
            </Text>
            <Text fw={500}>{formatMoney(receipt.total)}</Text>
          </Group>
          <Group justify="space-between" wrap="nowrap" gap="xs">
            <Text size="sm" c="dimmed" truncate>
              {date}
            </Text>
            <StatusBadge status={receipt.status} />
          </Group>
        </Stack>
      </UnstyledButton>
      <Anchor
        href={pathFor({ page: 'receipt', receiptId: receipt.id })}
        size="sm"
        className={classes.open}
        aria-label={`Open ${receipt.storeName ?? 'Unknown store'}, ${date}`}
        onClick={(event) => {
          event.preventDefault()
          onOpen()
        }}
      >
        Open
      </Anchor>
    </div>
  )
}
