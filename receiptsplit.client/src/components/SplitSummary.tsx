import { Card, Table, Text, Title } from '@mantine/core'
import { discountLabel, formatCents, type SplitSummary as Summary } from '../splits.ts'

function plural(count: number, noun: string): string {
  return `${count} ${noun}${count === 1 ? '' : 's'}`
}

export function SplitSummary({ summary }: { summary: Summary }) {
  const { people, assignedCents, linesCents, receiptTotalCents, unassigned, mismatched } = summary
  // A column for each receipt with a storewide discount, named after it when there are several receipts.
  const discounted = summary.receipts.filter((receipt) => receipt.discountPercent !== 0)
  const done = unassigned === 0 && mismatched === 0

  return (
    <Card withBorder padding="md">
      <Title order={2} mb="xs">
        Who owes what
      </Title>
      {people.length === 0 ? (
        <Text size="sm" c="dimmed">
          Add names to start splitting.
        </Text>
      ) : (
        <Table verticalSpacing={6} style={{ fontVariantNumeric: 'tabular-nums' }}>
          <Table.Thead>
            <Table.Tr>
              <Table.Th>Person</Table.Th>
              {discounted.length > 0 && <Table.Th ta="right">Items</Table.Th>}
              {discounted.map((receipt) => (
                <Table.Th key={receipt.id} ta="right">
                  {discountLabel(summary, receipt)}
                </Table.Th>
              ))}
              <Table.Th ta="right">Owes</Table.Th>
            </Table.Tr>
          </Table.Thead>
          <Table.Tbody>
            {people.map(({ person, cents, itemsCents, discounts }) => (
              <Table.Tr key={person}>
                <Table.Td>{person}</Table.Td>
                {discounted.length > 0 && <Table.Td ta="right">{formatCents(itemsCents)}</Table.Td>}
                {discounted.map((receipt) => (
                  <Table.Td key={receipt.id} ta="right">
                    {formatCents(discounts[receipt.id] ?? 0)}
                  </Table.Td>
                ))}
                <Table.Td ta="right">{formatCents(cents)}</Table.Td>
              </Table.Tr>
            ))}
          </Table.Tbody>
        </Table>
      )}
      <Text size="sm" mt="sm" c={done ? 'green' : 'dimmed'}>
        {done ? '✓ ' : ''}Assigned {formatCents(assignedCents)} of {formatCents(linesCents)}
        {receiptTotalCents != null &&
          receiptTotalCents !== linesCents &&
          ` (the receipt says ${formatCents(receiptTotalCents)})`}
        {unassigned > 0 && ` — ${plural(unassigned, 'item')} not assigned`}
        {mismatched > 0 && ` — ${plural(mismatched, 'item')} with amounts that don't add up`}
      </Text>
    </Card>
  )
}
