import { useState } from 'react'
import { Button, Group, NumberInput, Stack, Text } from '@mantine/core'
import { formatCents, toCents, type SplitLine } from '../splits.ts'

interface SplitAmountFormProps {
  line: SplitLine
  person: string
  /** The person's amount so far, in cents, or null when they have none. */
  current: number | null
  /** What the other people's amounts leave of the line, in cents. */
  remaining: number
  onSave: (cents: number) => void
  onRemove: () => void
  onCancel: () => void
}

/** Modal body for entering what one person pays towards a line, tax included. */
export function SplitAmountForm({ line, person, current, remaining, onSave, onRemove, onCancel }: SplitAmountFormProps) {
  const initial = current ?? (Math.sign(remaining) === Math.sign(line.cents) && remaining !== 0 ? remaining : line.cents)
  const [value, setValue] = useState<string | number>(initial / 100)
  const low = Math.min(0, line.cents) / 100
  const high = Math.max(0, line.cents) / 100
  const number = Number(value)
  const valid = String(value).trim() !== '' && number !== 0 && number >= low && number <= high

  return (
    <form
      onSubmit={(event) => {
        event.preventDefault()
        if (valid) {
          onSave(toCents(number))
        }
      }}
    >
      <Stack gap="sm">
        <Text size="sm">
          {person}'s part of {line.name}, out of {formatCents(line.cents)}
          {line.isTaxed ? ' including tax' : ''}. The others leave {formatCents(remaining)}.
        </Text>
        <NumberInput
          label="Amount"
          prefix="$"
          decimalScale={2}
          fixedDecimalScale
          min={low}
          max={high}
          data-autofocus
          value={value}
          onChange={setValue}
        />
        <Group justify="space-between">
          <Button variant="default" c="red" onClick={onRemove} disabled={current == null}>
            Remove
          </Button>
          <Group gap="xs">
            <Button variant="default" onClick={onCancel}>
              Cancel
            </Button>
            <Button type="submit" disabled={!valid}>
              Save
            </Button>
          </Group>
        </Group>
      </Stack>
    </form>
  )
}
