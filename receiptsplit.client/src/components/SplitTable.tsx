import { Button, Card, Group, Table, Text } from '@mantine/core'
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
import type { SplitActions } from '../useSplitState.ts'
import { SplitAmountForm } from './SplitAmountForm.tsx'

interface SplitTableProps {
  lines: SplitLine[]
  state: SplitState
  actions: SplitActions
}

const problemColors: Record<LineProblem, string> = {
  unassigned: 'var(--mantine-color-yellow-light)',
  mismatch: 'var(--mantine-color-red-light)',
}

export function SplitTable({ lines, state, actions }: SplitTableProps) {
  const { people } = state

  function enterAmount(line: SplitLine, assignment: LineAssignment, person: string) {
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
    <Card withBorder padding="md">
      <Text size="sm" c="dimmed" mb="xs">
        Click a name to give them a share of an item; − or a right click takes one away. Shares split the item's total,
        tax included, evenly. Turn on $ Amounts to enter what each person pays instead.
      </Text>
      <Table.ScrollContainer minWidth={640}>
        <Table verticalSpacing="xs" style={{ fontVariantNumeric: 'tabular-nums' }}>
          <Table.Thead>
            <Table.Tr>
              <Table.Th w={40}>#</Table.Th>
              <Table.Th>Item</Table.Th>
              <Table.Th ta="right">Qty</Table.Th>
              <Table.Th ta="right">Total</Table.Th>
              <Table.Th>Shares</Table.Th>
            </Table.Tr>
          </Table.Thead>
          <Table.Tbody>
            {lines.map((line) => {
              const assignment = assignmentFor(state, line.position)
              const split = lineSplit(line, assignment, people)
              const byAmounts = assignment.mode === 'amounts'
              return (
                <Table.Tr key={line.position} bg={split.problem ? problemColors[split.problem] : undefined}>
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
                        promotion {formatMoney(line.discount)}
                      </Text>
                    )}
                  </Table.Td>
                  <Table.Td ta="right">{line.quantity}</Table.Td>
                  <Table.Td
                    ta="right"
                    style={{ whiteSpace: 'nowrap' }}
                    title={
                      split.problem === 'mismatch'
                        ? `Amounts add up to ${formatCents(split.assignedCents)}, but the item is ${formatCents(line.cents)}`
                        : undefined
                    }
                  >
                    {formatCents(line.cents)}
                    {line.isTaxed && (
                      <Text size="xs" c="dimmed">
                        incl. tax
                      </Text>
                    )}
                  </Table.Td>
                  <Table.Td>
                    {people.length === 0 ? (
                      <Text size="sm" c="dimmed">
                        Add names above
                      </Text>
                    ) : (
                      <Group gap={6}>
                        {people.map((person) => {
                          if (byAmounts) {
                            const cents = assignment.amounts[person]
                            return (
                              <Button
                                key={person}
                                size="xs"
                                miw={64}
                                variant={cents != null ? 'filled' : 'default'}
                                color="green"
                                onClick={() => enterAmount(line, assignment, person)}
                                onContextMenu={(event) => {
                                  event.preventDefault()
                                  actions.setAmount(line.position, person, null)
                                }}
                              >
                                {cents != null ? `${person} $${formatCents(cents)}` : person}
                              </Button>
                            )
                          }

                          const count = assignment.shares[person] ?? 0
                          return (
                            <Button.Group key={person}>
                              <Button
                                size="xs"
                                miw={64}
                                variant={count > 0 ? 'filled' : 'default'}
                                color="green"
                                onClick={() => actions.changeShare(line.position, person, 1)}
                                onContextMenu={(event) => {
                                  event.preventDefault()
                                  actions.changeShare(line.position, person, -1)
                                }}
                              >
                                {count > 1 ? `${person} ×${count}` : person}
                              </Button>
                              {count > 0 && (
                                <Button
                                  size="xs"
                                  px={8}
                                  color="green.8"
                                  aria-label={`Take a share of ${line.name} from ${person}`}
                                  onClick={() => actions.changeShare(line.position, person, -1)}
                                >
                                  −
                                </Button>
                              )}
                            </Button.Group>
                          )
                        })}
                        <Button
                          size="xs"
                          ml="xs"
                          variant={byAmounts ? 'filled' : 'default'}
                          aria-pressed={byAmounts}
                          onClick={() => actions.setMode(line.position, byAmounts ? 'shares' : 'amounts')}
                        >
                          $ Amounts
                        </Button>
                        <Button
                          size="xs"
                          variant="default"
                          disabled={byAmounts}
                          onClick={() => actions.addEveryone(line.position)}
                        >
                          All
                        </Button>
                        <Button size="xs" variant="default" onClick={() => actions.clearLine(line.position)}>
                          Clear
                        </Button>
                      </Group>
                    )}
                  </Table.Td>
                </Table.Tr>
              )
            })}
          </Table.Tbody>
        </Table>
      </Table.ScrollContainer>
    </Card>
  )
}
