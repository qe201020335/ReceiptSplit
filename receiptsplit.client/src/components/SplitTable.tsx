import { Fragment } from 'react'
import { ActionIcon, Button, Card, Group, Indicator, Menu, Table, Text } from '@mantine/core'
import { useMediaQuery } from '@mantine/hooks'
import { modals } from '@mantine/modals'
import { formatMoney } from '../format.ts'
import {
  assignmentFor,
  formatCents,
  lineSplit,
  type LineAssignment,
  type LineProblem,
  type SplitLine,
  type SplitState,
} from '../splits.ts'
import { useSecondaryPress } from '../useSecondaryPress.ts'
import type { SplitActions } from '../useSplitState.ts'
import { SplitAmountForm } from './SplitAmountForm.tsx'
import classes from './SplitTable.module.css'

interface SplitTableProps {
  lines: SplitLine[]
  state: SplitState
  actions: SplitActions
}

// Filled buttons use fixed dark shades: the theme's dark-mode shade 4 is too light for white text.
// Row tints are faint enough to keep the buttons readable in both color schemes.
const problemColors: Record<LineProblem, string> = {
  unassigned: 'color-mix(in srgb, var(--mantine-color-yellow-5) 18%, transparent)',
  mismatch: 'color-mix(in srgb, var(--mantine-color-red-6) 22%, transparent)',
}

export function SplitTable({ lines, state, actions }: SplitTableProps) {
  // On a phone the share buttons get a full-width row under their item instead of a squeezed column. Read the
  // query while rendering, not after, so a phone doesn't paint the wide layout first.
  const stacked = useMediaQuery('(max-width: 48em)', undefined, { getInitialValueInEffect: false })

  return (
    <Card withBorder padding="md">
      <Text size="sm" c="dimmed" mb="xs">
        Click or tap a name to give them a share of an item; right click or long-press to take one away. Shares split
        the item's total, tax included, evenly. An item's ⋯ menu splits it by dollar amounts instead, gives everyone a
        share, or clears it.
      </Text>
      <Table.ScrollContainer minWidth={stacked ? 0 : 640}>
        <Table verticalSpacing="xs" style={{ fontVariantNumeric: 'tabular-nums' }}>
          <Table.Thead>
            <Table.Tr>
              {!stacked && <Table.Th w={40}>#</Table.Th>}
              <Table.Th>Item</Table.Th>
              <Table.Th ta="right">Qty</Table.Th>
              <Table.Th ta="right">Total</Table.Th>
              {!stacked && <Table.Th>Shares</Table.Th>}
              <Table.Th w={1}>
                <span className={classes.visuallyHidden}>More</span>
              </Table.Th>
            </Table.Tr>
          </Table.Thead>
          <Table.Tbody>
            {lines.map((line) => {
              const assignment = assignmentFor(state, line.position)
              const split = lineSplit(line, assignment, state.people)
              const background = split.problem ? problemColors[split.problem] : undefined
              const shares = (
                <ShareButtons line={line} assignment={assignment} people={state.people} actions={actions} />
              )
              const mismatch =
                split.problem === 'mismatch'
                  ? `Amounts add up to ${formatCents(split.assignedCents)}, but the item is ${formatCents(line.cents)}`
                  : undefined

              return (
                <Fragment key={line.position}>
                  <Table.Tr bg={background} style={stacked ? { borderBottom: 0 } : undefined}>
                    {!stacked && <Table.Td c="dimmed">{line.position + 1}</Table.Td>}
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
                          promotion {formatMoney(line.discount)}
                        </Text>
                      )}
                    </Table.Td>
                    <Table.Td ta="right">{line.quantity}</Table.Td>
                    <Table.Td ta="right" style={{ whiteSpace: 'nowrap' }} title={mismatch}>
                      {formatCents(line.cents)}
                      {line.isTaxed && (
                        <Text size="xs" c="dimmed">
                          incl. tax
                        </Text>
                      )}
                      {assignment.mode === 'amounts' && (
                        <Text size="xs" c="brand">
                          $ amounts
                        </Text>
                      )}
                    </Table.Td>
                    {!stacked && <Table.Td>{shares}</Table.Td>}
                    <Table.Td>
                      <RowMenu
                        line={line}
                        assignment={assignment}
                        hasPeople={state.people.length > 0}
                        actions={actions}
                      />
                    </Table.Td>
                  </Table.Tr>
                  {stacked && (
                    <Table.Tr bg={background}>
                      <Table.Td colSpan={4} pt={0}>
                        {shares}
                      </Table.Td>
                    </Table.Tr>
                  )}
                </Fragment>
              )
            })}
          </Table.Tbody>
        </Table>
      </Table.ScrollContainer>
    </Card>
  )
}

interface RowMenuProps {
  line: SplitLine
  assignment: LineAssignment
  hasPeople: boolean
  actions: SplitActions
}

/** The less frequent per-item actions, kept out of the row so the name buttons have the room. */
function RowMenu({ line, assignment, hasPeople, actions }: RowMenuProps) {
  const byAmounts = assignment.mode === 'amounts'

  return (
    <Menu position="bottom-end">
      <Menu.Target>
        <ActionIcon variant="subtle" color="gray" aria-label={`More ways to split ${line.name}`}>
          ⋯
        </ActionIcon>
      </Menu.Target>
      <Menu.Dropdown>
        <Menu.Item
          leftSection={<span className={classes.check}>{byAmounts ? '✓' : ''}</span>}
          onClick={() => actions.setMode(line.position, byAmounts ? 'shares' : 'amounts')}
        >
          Split by $ amounts
        </Menu.Item>
        <Menu.Item
          leftSection={<span className={classes.check} />}
          disabled={byAmounts || !hasPeople}
          onClick={() => actions.addEveryone(line.position)}
        >
          Give everyone a share
        </Menu.Item>
        <Menu.Item leftSection={<span className={classes.check} />} onClick={() => actions.clearLine(line.position)}>
          Clear
        </Menu.Item>
      </Menu.Dropdown>
    </Menu>
  )
}

interface ShareButtonsProps {
  line: SplitLine
  assignment: LineAssignment
  people: string[]
  actions: SplitActions
}

function ShareButtons({ line, assignment, people, actions }: ShareButtonsProps) {
  const byAmounts = assignment.mode === 'amounts'

  if (people.length === 0) {
    return (
      <Text size="sm" c="dimmed">
        Add names above
      </Text>
    )
  }

  function enterAmount(person: string) {
    const others = Object.entries(assignment.amounts)
      .filter(([other]) => other !== person)
      .reduce((sum, [, cents]) => sum + cents, 0)
    const modalId = `split-amount-${line.position}`
    modals.open({
      modalId,
      title: `${person} pays towards ${line.name}`,
      centered: true,
      children: (
        <SplitAmountForm
          line={line}
          person={person}
          current={assignment.amounts[person] ?? null}
          remaining={line.cents - others}
          onSave={(cents) => {
            actions.setAmount(line.position, person, cents)
            modals.close(modalId)
          }}
          onRemove={() => {
            actions.setAmount(line.position, person, null)
            modals.close(modalId)
          }}
          onCancel={() => modals.close(modalId)}
        />
      ),
    })
  }

  return (
    <Group gap={6}>
      {people.map((person) => {
        if (byAmounts) {
          const cents = assignment.amounts[person]
          return (
            <PersonButton
              key={person}
              active={cents != null}
              label={cents != null ? `${person} $${formatCents(cents)}` : person}
              onPress={() => enterAmount(person)}
              onSecondary={() => actions.setAmount(line.position, person, null)}
            />
          )
        }

        const count = assignment.shares[person] ?? 0
        return (
          <PersonButton
            key={person}
            active={count > 0}
            count={count}
            label={person}
            onPress={() => actions.changeShare(line.position, person, 1)}
            onSecondary={() => actions.changeShare(line.position, person, -1)}
          />
        )
      })}
    </Group>
  )
}

interface PersonButtonProps {
  active: boolean
  /** Shares held, shown as a corner badge from two up so the button never changes width. */
  count?: number
  label: string
  onPress: () => void
  /** Right click, long press, or Delete: take a share or the amount away. */
  onSecondary: () => void
}

function PersonButton({ active, count = 0, label, onPress, onSecondary }: PersonButtonProps) {
  const secondary = useSecondaryPress(() => {
    if (active) {
      onSecondary()
    }
  })

  return (
    <Indicator
      inline
      label={count}
      disabled={count < 2}
      size={16}
      offset={2}
      classNames={{ indicator: classes.count }}
    >
      <Button
        size="xs"
        className={classes.person}
        variant={active ? 'filled' : 'default'}
        // Color alone doesn't tell a screen reader who already has a share.
        aria-pressed={active}
        aria-label={count > 1 ? `${label}, ${count} shares` : undefined}
        color="green.9"
        onClick={onPress}
        {...secondary}
      >
        {label}
      </Button>
    </Indicator>
  )
}
