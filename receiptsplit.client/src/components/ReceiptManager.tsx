import { useEffect, useState, type MouseEvent, type ReactNode } from 'react'
import {
  Alert,
  Anchor,
  Button,
  Card,
  Checkbox,
  Group,
  Select,
  Stack,
  Text,
  Title,
  UnstyledButton,
} from '@mantine/core'
import { modals } from '@mantine/modals'
import { notifications } from '@mantine/notifications'
import { api, errorMessage, type Account, type ReceiptSummary, type UserSummary } from '../api.ts'
import { formatDateTime, formatMoney } from '../format.ts'
import { ownerFilter, userLabel } from '../owners.ts'
import { usePageTitle } from '../usePageTitle.ts'
import { pathFor } from '../useRoute.ts'
import { OwnerAvatar } from './OwnerAvatar.tsx'
import { OwnerPicker } from './OwnerPicker.tsx'
import { StatusBadge } from './StatusBadge.tsx'
import classes from './ReceiptManager.module.css'

interface ReceiptManagerProps {
  /** The signed-in user; admins also see and filter by each receipt's owner. */
  account: Account | null
  receipts: ReceiptSummary[] | null
  error: string | null
  /** The owner filter from the URL: 'none', a user id, or null for everyone. Only admins filter. */
  owner: string | null
  onOwnerChange: (owner: string | null) => void
  /** Called after receipts were deleted or reassigned, to reload the list. */
  onChanged: () => void
  onOpen: (id: string) => void
  onBack: () => void
}

interface Month {
  key: string
  label: string
  receipts: ReceiptSummary[]
}

/** The owner filter's value for everyone; Select needs a string for it. */
const everyone = 'everyone'

function receiptCount(count: number): string {
  return `${count} receipt${count === 1 ? '' : 's'}`
}

/** "a", "a and b", "a, b and c". */
function joined(parts: string[]): string {
  return parts.length <= 1 ? parts.join('') : `${parts.slice(0, -1).join(', ')} and ${parts.at(-1)}`
}

/**
 * Whose the receipts are now, for confirming a bulk reassign: "7 have no owner and 3 are Alice Chen's", or "All 10
 * are Ben Kim's".
 */
function ownerBreakdown(receipts: ReceiptSummary[], nameOf: (ownerId: string) => string): string {
  const counts = new Map<string | null, number>()
  for (const { ownerId } of receipts) {
    counts.set(ownerId, (counts.get(ownerId) ?? 0) + 1)
  }
  const describe = (ownerId: string | null, count: number, all = false) => {
    const subject = !all ? String(count) : count === 1 ? 'It' : count === 2 ? 'Both' : `All ${count}`
    const one = count === 1
    return ownerId === null
      ? `${subject} ${one ? 'has' : 'have'} no owner`
      : `${subject} ${one ? 'is' : 'are'} ${nameOf(ownerId)}'s`
  }
  if (counts.size === 1) {
    const [[ownerId, count]] = counts
    return `${describe(ownerId, count, true)}.`
  }
  // No owner first, then the largest groups.
  const groups = [...counts].sort(([a, x], [b, y]) => (a === null ? -1 : b === null ? 1 : y - x))
  return `${joined(groups.map(([ownerId, count]) => describe(ownerId, count)))}.`
}

/** The date a row shows: the purchase date, or when it was added for a receipt that has none. */
function dateOf(receipt: ReceiptSummary): string {
  return receipt.purchaseDate ?? formatDateTime(receipt.createdAt)
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

/** Every receipt, to select several and delete them; admins also see whose each one is and filter by owner. */
export function ReceiptManager({
  account,
  receipts,
  error,
  owner,
  onOwnerChange,
  onChanged,
  onOpen,
  onBack,
}: ReceiptManagerProps) {
  const admin = account?.isAdmin === true
  const [selected, setSelected] = useState<ReadonlySet<string>>(() => new Set())
  const [deleting, setDeleting] = useState(false)
  const [reassigning, setReassigning] = useState(false)
  const busy = deleting || reassigning
  const [users, setUsers] = useState<UserSummary[] | null>(null)
  const [usersError, setUsersError] = useState<string | null>(null)
  usePageTitle('Manage receipts')

  useEffect(() => {
    if (!admin) {
      return
    }
    let current = true
    api.listUsers().then(
      (loaded) => {
        if (current) {
          setUsers(loaded)
          setUsersError(null)
        }
      },
      (e: unknown) => {
        if (current) {
          setUsersError(errorMessage(e))
        }
      },
    )
    return () => {
      current = false
    }
  }, [admin])

  const all = receipts ?? []
  const filter = admin ? ownerFilter(owner, users) : null
  const shown = filter === null ? all : all.filter((receipt) => (receipt.ownerId ?? 'none') === filter)
  // In list order, and only receipts the filter shows: hidden, deleted or reassigned ones are never acted on.
  const selectedReceipts = shown.filter((receipt) => selected.has(receipt.id))
  const selectedIds = selectedReceipts.map(({ id }) => id)
  // The server won't delete a receipt while the model is reading it, so Delete waits until it's read.
  const readingCount = selectedReceipts.filter((receipt) => receipt.status === 'Processing').length
  const months = byMonth(shown)
  const usersById = new Map((users ?? []).map((user) => [user.id, user]))
  const filterUser = filter === null || filter === 'none' ? undefined : usersById.get(filter)

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
      // the reload drops receipts that are gone from it.
      notifications.show({ title: "Couldn't delete the receipts", message: errorMessage(e), color: 'red' })
    } finally {
      setDeleting(false)
      onChanged()
    }
  }

  function nameOf(ownerId: string): string {
    const user = usersById.get(ownerId)
    return user ? userLabel(user) : 'Unknown user'
  }

  function confirmReassign(ownerId: string | null) {
    const chosen = selectedReceipts
    const count = receiptCount(chosen.length)
    const target = ownerId === null ? 'no owner' : nameOf(ownerId)
    modals.openConfirmModal({
      title: `Reassign ${count} to ${target}?`,
      centered: true,
      children: <Text size="sm">{ownerBreakdown(chosen, nameOf)}</Text>,
      labels: { confirm: `Reassign ${count}`, cancel: 'Cancel' },
      onConfirm: () => void reassign(chosen.map(({ id }) => id), ownerId),
    })
  }

  async function reassign(ids: string[], ownerId: string | null) {
    const target = ownerId === null ? null : nameOf(ownerId)
    const several = ids.length > 1
    setReassigning(true)
    try {
      await api.reassignReceipts(ids, ownerId)
      if (several) {
        setSelected(new Set())
      }
      const message = several
        ? `${receiptCount(ids.length)} ${target === null ? 'set to no owner' : `reassigned to ${target}`}`
        : target === null
          ? 'Set to no owner'
          : `Reassigned to ${target}`
      notifications.show({ message, color: 'green' })
    } catch (e) {
      // Nothing was reassigned, and the message says why; the reload shows what changed meanwhile.
      const title = several ? "Couldn't reassign the receipts" : "Couldn't reassign the receipt"
      notifications.show({ title, message: errorMessage(e), color: 'red' })
    } finally {
      setReassigning(false)
      onChanged()
    }
  }

  /** The owner line of an admin's row; undefined for members, whose rows have none. */
  function ownerOf(receipt: ReceiptSummary): OwnerLine | undefined {
    if (!admin) {
      return undefined
    }
    if (receipt.ownerId === null) {
      return { user: null, label: 'No owner' }
    }
    const user = usersById.get(receipt.ownerId)
    if (user) {
      return { user, label: userLabel(user) }
    }
    // Still loading the people, or someone the list doesn't have.
    return { user: null, label: users === null && !usersError ? '…' : 'Unknown user' }
  }

  const ownerChoices = admin
    ? [
        { value: everyone, label: `Everyone (${all.length})` },
        { value: 'none', label: `No owner (${all.filter((r) => r.ownerId === null).length})` },
        ...(users ?? []).map((user) => ({
          value: user.id,
          label: `${userLabel(user)} (${all.filter((r) => r.ownerId === user.id).length})`,
        })),
      ]
    : []

  return (
    <Stack gap="md">
      <Card withBorder padding="md">
        <Group gap="md" justify="space-between">
          <Group gap="md" wrap="nowrap" miw={0}>
            <Button variant="default" onClick={onBack} style={{ flexShrink: 0 }}>
              ← Back
            </Button>
            <Stack gap={0} miw={0}>
              <Title order={2}>Manage receipts</Title>
              {all.length > 0 && (
                <Text size="sm" c="dimmed">
                  {filter === null ? receiptCount(all.length) : `${shown.length} of ${receiptCount(all.length)}`}
                </Text>
              )}
            </Stack>
          </Group>
          {admin && (
            <Select
              label="Owner"
              data={ownerChoices}
              value={filter ?? everyone}
              onChange={(value) => onOwnerChange(value === null || value === everyone ? null : value)}
              allowDeselect={false}
              w={{ base: '100%', xs: 240 }}
              classNames={{ root: classes.filter, label: classes.filterLabel, wrapper: classes.filterInput }}
            />
          )}
        </Group>
      </Card>
      {error && (
        <Alert color="red" variant="light">
          {error}
        </Alert>
      )}
      {usersError && (
        <Alert color="red" variant="light" title="Couldn't load people">
          {usersError}
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
      {all.length > 0 && shown.length === 0 && (
        <Card withBorder padding="xl">
          <Text c="dimmed" ta="center">
            {filter === 'none'
              ? 'Every receipt has an owner.'
              : `${filterUser ? userLabel(filterUser) : 'This person'} has no receipts.`}{' '}
            <Anchor
              href={pathFor({ page: 'manage', owner: null })}
              onClick={(event) => {
                event.preventDefault()
                onOwnerChange(null)
              }}
            >
              Show everyone's
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
                    checked={checkedCount === ids.length}
                    indeterminate={checkedCount > 0 && checkedCount < ids.length}
                    disabled={busy}
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
                    owner={ownerOf(receipt)}
                    checked={selected.has(receipt.id)}
                    disabled={busy}
                    onToggle={(on) => toggle([receipt.id], on)}
                    onOpen={() => onOpen(receipt.id)}
                    reassign={
                      admin && (
                        <OwnerPicker
                          users={users}
                          usersError={usersError}
                          currentOwnerId={receipt.ownerId}
                          meId={account.id}
                          onPick={(ownerId) => void reassign([receipt.id], ownerId)}
                        >
                          {(toggleOpen) => (
                            <Button
                              size="xs"
                              variant="default"
                              disabled={busy}
                              aria-label={`Reassign ${receipt.storeName ?? 'Unknown store'}, ${dateOf(receipt)}`}
                              onClick={toggleOpen}
                            >
                              Reassign
                            </Button>
                          )}
                        </OwnerPicker>
                      )
                    }
                  />
                ))}
              </section>
            )
          })}
          {selectedIds.length > 0 && (
            // The count is in the action buttons, which leaves the footer room for all three on a phone.
            <div className={classes.footer}>
              <Button variant="default" disabled={busy} onClick={() => setSelected(new Set())}>
                Clear
              </Button>
              <Group gap="xs" wrap="nowrap">
                {admin && (
                  <OwnerPicker users={users} usersError={usersError} meId={account.id} onPick={confirmReassign}>
                    {(toggleOpen) => (
                      <Button
                        variant="default"
                        loading={reassigning}
                        disabled={deleting}
                        aria-label={`Reassign ${receiptCount(selectedIds.length)}`}
                        onClick={toggleOpen}
                      >
                        <CountLabel verb="Reassign" count={selectedIds.length} />
                      </Button>
                    )}
                  </OwnerPicker>
                )}
                {/* Not disabled while a selected receipt is being read: it stays clickable, to say why. */}
                <Button
                  color="red"
                  loading={deleting}
                  disabled={reassigning}
                  data-disabled={readingCount > 0 || undefined}
                  aria-disabled={readingCount > 0 || undefined}
                  aria-label={`Delete ${receiptCount(selectedIds.length)}`}
                  onClick={confirmDelete}
                >
                  <CountLabel verb="Delete" count={selectedIds.length} />
                </Button>
              </Group>
            </div>
          )}
        </Card>
      )}
    </Stack>
  )
}

/**
 * "Delete 3 receipts", shortened to "Delete 3" on phones, where the footer's three buttons have to fit a 3-digit
 * count. The button's aria-label keeps the whole phrase.
 */
function CountLabel({ verb, count }: { verb: string; count: number }) {
  return (
    <>
      {verb} {count}
      <span className={classes.countNoun}> {count === 1 ? 'receipt' : 'receipts'}</span>
    </>
  )
}

/** Who owns a receipt, as an admin's row shows it. */
interface OwnerLine {
  /** Null for no owner, or while the person can't be named. */
  user: UserSummary | null
  label: string
}

interface ReceiptManagerRowProps {
  receipt: ReceiptSummary
  /** Only admins' rows have an owner line, with its Reassign button. */
  owner: OwnerLine | undefined
  reassign: ReactNode
  checked: boolean
  disabled: boolean
  onToggle: (on: boolean) => void
  onOpen: () => void
}

/**
 * One receipt: the row itself selects it, and Open sits beside it rather than inside, so the two never clash. An
 * admin's row adds the owner on a line of its own below the toggle, and Open and Reassign as buttons beside both.
 */
function ReceiptManagerRow({ receipt, owner, reassign, checked, disabled, onToggle, onOpen }: ReceiptManagerRowProps) {
  const date = dateOf(receipt)
  const href = pathFor({ page: 'receipt', receiptId: receipt.id })
  const openLabel = `Open ${receipt.storeName ?? 'Unknown store'}, ${date}`
  return (
    <div className={classes.row} data-checked={checked || undefined} data-with-owner={owner ? true : undefined}>
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
      {owner ? (
        // An admin's row has two actions, so they're buttons stacked beside the whole row rather than links.
        <div className={classes.actions}>
          <Button
            component="a"
            href={href}
            size="xs"
            variant="default"
            aria-label={openLabel}
            onClick={(event: MouseEvent<HTMLAnchorElement>) => {
              event.preventDefault()
              onOpen()
            }}
          >
            Open
          </Button>
          {reassign}
        </div>
      ) : (
        <Anchor
          href={href}
          size="sm"
          className={classes.open}
          aria-label={openLabel}
          onClick={(event) => {
            event.preventDefault()
            onOpen()
          }}
        >
          Open
        </Anchor>
      )}
      {owner && (
        <div className={classes.owner}>
          <OwnerAvatar user={owner.user} />
          <Text size="sm" truncate className={owner.user ? undefined : classes.noOwner}>
            {owner.label}
          </Text>
        </div>
      )}
    </div>
  )
}
