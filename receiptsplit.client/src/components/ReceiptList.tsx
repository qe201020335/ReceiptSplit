import { Alert, Button, Card, Checkbox, Group, NavLink, Stack, Text, Title } from '@mantine/core'
import { useHotkeys } from '@mantine/hooks'
import type { ReceiptSummary } from '../api.ts'
import { formatDateTime, formatMoney } from '../format.ts'
import { StatusBadge } from './StatusBadge.tsx'
import classes from './ReceiptList.module.css'

interface ReceiptListProps {
  receipts: ReceiptSummary[] | null
  error: string | null
  selectedId: string | null
  onSelect: (id: string) => void
  /** Receipts picked to split together, or null when not picking. */
  picked: string[] | null
  onPickedChange: (ids: string[] | null) => void
  onSplit: (ids: string[]) => void
}

/** Only receipts whose lines agree with their totals can be split, alone or together. */
function splittable(receipt: ReceiptSummary): boolean {
  return receipt.status === 'Completed'
}

export function ReceiptList({
  receipts,
  error,
  selectedId,
  onSelect,
  picked,
  onPickedChange,
  onSplit,
}: ReceiptListProps) {
  const picking = picked !== null
  // In list order, and without receipts that were deleted or stopped checking out since they were picked.
  const pickedIds = picking
    ? (receipts ?? []).filter((receipt) => splittable(receipt) && picked.includes(receipt.id)).map(({ id }) => id)
    : []
  const anySplittable = receipts?.some(splittable) ?? false

  useHotkeys(picking ? [['Escape', () => onPickedChange(null)]] : [])

  function toggle(id: string) {
    onPickedChange(pickedIds.includes(id) ? pickedIds.filter((other) => other !== id) : [...pickedIds, id])
  }

  return (
    <Card withBorder padding="md" component="nav" aria-label="Receipts" className={classes.card}>
      <Stack gap="sm">
        <Group justify="space-between" wrap="nowrap" gap="xs" mih={26}>
          <Title order={2}>Receipts</Title>
          {picking ? (
            <Group gap="xs" wrap="nowrap">
              <Text size="sm" c="dimmed">
                {pickedIds.length} selected
              </Text>
              <Button variant="subtle" size="compact-sm" onClick={() => onPickedChange(null)}>
                Cancel
              </Button>
            </Group>
          ) : (
            anySplittable && (
              <Button variant="subtle" size="compact-sm" onClick={() => onPickedChange([])}>
                Split several
              </Button>
            )
          )}
        </Group>
        {picking && (
          <Text size="sm" c="dimmed">
            Pick the receipts to split together. Receipts still being read or needing review can't be picked.
          </Text>
        )}
        {error && (
          <Alert color="red" variant="light">
            {error}
          </Alert>
        )}
        {receipts === null && !error && <Text c="dimmed">Loading…</Text>}
        {receipts?.length === 0 && <Text c="dimmed">No receipts yet.</Text>}
        <Stack gap={2}>
          {receipts?.map((receipt) => {
            const label = (
              <Group justify="space-between" wrap="nowrap" gap="xs">
                <Text fw={500} truncate>
                  {receipt.storeName ?? 'Unknown store'}
                </Text>
                <Text fw={500}>{formatMoney(receipt.total)}</Text>
              </Group>
            )
            const description = (
              <Group justify="space-between" wrap="nowrap" gap="xs">
                <span>{receipt.purchaseDate ?? formatDateTime(receipt.createdAt)}</span>
                <StatusBadge status={receipt.status} />
              </Group>
            )

            if (picking) {
              const canPick = splittable(receipt)
              const checked = pickedIds.includes(receipt.id)
              return (
                <NavLink
                  key={receipt.id}
                  component="button"
                  type="button"
                  role="checkbox"
                  aria-checked={checked}
                  // NavLink's disabled only styles the row and blocks the pointer; the keyboard still reaches it.
                  disabled={!canPick}
                  aria-disabled={!canPick || undefined}
                  classNames={{ root: classes.item }}
                  onClick={canPick ? () => toggle(receipt.id) : undefined}
                  leftSection={<Checkbox.Indicator checked={checked} disabled={!canPick} />}
                  label={label}
                  description={description}
                />
              )
            }

            const open = receipt.id === selectedId
            return (
              <NavLink
                key={receipt.id}
                component={open ? 'div' : 'button'}
                type={open ? undefined : 'button'}
                active={open}
                aria-current={open ? 'page' : undefined}
                classNames={{ root: classes.item }}
                onClick={open ? undefined : () => onSelect(receipt.id)}
                label={label}
                description={description}
              />
            )
          })}
        </Stack>
        {picking && (
          <div className={classes.footer}>
            <Button fullWidth disabled={pickedIds.length === 0} onClick={() => onSplit(pickedIds)}>
              {pickedIds.length === 0
                ? 'Split receipts'
                : `Split ${pickedIds.length} receipt${pickedIds.length === 1 ? '' : 's'}`}
            </Button>
          </div>
        )}
      </Stack>
    </Card>
  )
}
