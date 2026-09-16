import { useState } from 'react'
import { ActionIcon, Button, Checkbox, Group, Stack, Table, Text, TextInput } from '@mantine/core'
import { notifications } from '@mantine/notifications'
import { api, errorMessage, type ReceiptDetail, type ReceiptLineEdit } from '../api.ts'
import { formatMoney } from '../format.ts'

interface LineDraft {
  /** Stable across adds and removes, unlike the array index. */
  key: number
  name: string
  code: string
  quantity: string
  amount: string
  taxCode: string
  isTaxed: boolean
  discount: string
}

interface ReceiptEditorProps {
  receipt: ReceiptDetail
  onSaved: (receipt: ReceiptDetail) => void
  onCancel: () => void
}

/** Blank is null, a number is itself, anything else is undefined: not a number. */
function toNumber(text: string): number | null | undefined {
  const trimmed = text.trim()
  if (trimmed === '') {
    return null
  }

  const value = Number(trimmed)
  return Number.isFinite(value) ? value : undefined
}

function toText(value: number | null, decimals = 2): string {
  return value == null ? '' : value.toFixed(decimals)
}

function draftLines(receipt: ReceiptDetail): LineDraft[] {
  return receipt.lines.map((line, index) => ({
    key: index,
    discount: line.discount === 0 ? '' : line.discount.toFixed(2),
    name: line.name,
    code: line.code ?? '',
    quantity: String(line.quantity),
    amount: line.amount.toFixed(2),
    taxCode: line.taxCode ?? '',
    isTaxed: line.isTaxed,
  }))
}

const numberInput = { input: { textAlign: 'right' as const } }

export function ReceiptEditor({ receipt, onSaved, onCancel }: ReceiptEditorProps) {
  const [storeName, setStoreName] = useState(receipt.storeName ?? '')
  const [purchaseDate, setPurchaseDate] = useState(receipt.purchaseDate ?? '')
  const [subtotal, setSubtotal] = useState(toText(receipt.subtotal))
  const [tax, setTax] = useState(toText(receipt.tax))
  const [total, setTotal] = useState(toText(receipt.total))
  const [lines, setLines] = useState<LineDraft[]>(() => draftLines(receipt))
  const [nextKey, setNextKey] = useState(receipt.lines.length)
  const [saving, setSaving] = useState(false)

  function updateLine(key: number, change: Partial<LineDraft>) {
    setLines((current) => current.map((line) => (line.key === key ? { ...line, ...change } : line)))
  }

  function addLine() {
    setLines((current) => [
      ...current,
      { key: nextKey, name: '', code: '', quantity: '1', amount: '', taxCode: '', isTaxed: false, discount: '' },
    ])
    setNextKey((key) => key + 1)
  }

  const amounts = lines.map((line) => toNumber(line.amount))
  const linesSum = amounts.reduce((sum: number, amount) => sum + (amount ?? 0), 0)
  const subtotalValue = toNumber(subtotal)
  const difference = subtotalValue == null ? null : Math.round((linesSum - subtotalValue) * 100) / 100

  const invalid =
    lines.length === 0 ||
    lines.some(
      (line) =>
        line.name.trim() === '' ||
        toNumber(line.amount) == null ||
        toNumber(line.quantity) == null ||
        toNumber(line.discount) === undefined ||
        (toNumber(line.discount) ?? 0) > 0,
    ) ||
    [subtotal, tax, total].some((value) => toNumber(value) === undefined)

  async function save() {
    if (invalid) {
      return
    }

    setSaving(true)
    try {
      const edit = {
        storeName: storeName.trim() === '' ? null : storeName.trim(),
        purchaseDate: purchaseDate === '' ? null : purchaseDate,
        subtotal: toNumber(subtotal) ?? null,
        tax: toNumber(tax) ?? null,
        total: toNumber(total) ?? null,
        lines: lines.map(
          (line): ReceiptLineEdit => ({
            name: line.name.trim(),
            code: line.code.trim() === '' ? null : line.code.trim(),
            quantity: toNumber(line.quantity) ?? 1,
            amount: toNumber(line.amount) ?? 0,
            discount: toNumber(line.discount) ?? 0,
            taxCode: line.taxCode.trim() === '' ? null : line.taxCode.trim(),
            isTaxed: line.isTaxed,
          }),
        ),
      }
      onSaved(await api.updateReceipt(receipt.id, edit))
    } catch (e) {
      notifications.show({ title: "Couldn't save the corrections", message: errorMessage(e), color: 'red' })
      setSaving(false)
    }
  }

  return (
    <form
      onSubmit={(event) => {
        event.preventDefault()
        void save()
      }}
    >
      <Stack gap="sm">
        <Group gap="sm" align="flex-end">
          <TextInput
            label="Store"
            size="xs"
            w={200}
            maxLength={200}
            value={storeName}
            onChange={(event) => setStoreName(event.currentTarget.value)}
          />
          <TextInput
            label="Purchase date"
            size="xs"
            type="date"
            w={160}
            value={purchaseDate}
            onChange={(event) => setPurchaseDate(event.currentTarget.value)}
          />
        </Group>

        <Table.ScrollContainer minWidth={700}>
          <Table verticalSpacing={4} horizontalSpacing="xs">
            <Table.Thead>
              <Table.Tr>
                <Table.Th w={30}>#</Table.Th>
                <Table.Th>Item</Table.Th>
                <Table.Th w={90}>Code</Table.Th>
                <Table.Th w={90} ta="right">
                  Qty
                </Table.Th>
                <Table.Th w={100} ta="right">
                  Amount
                </Table.Th>
                <Table.Th w={100} ta="right">
                  Discount
                </Table.Th>
                <Table.Th w={80}>Tax code</Table.Th>
                <Table.Th w={60}>Taxed</Table.Th>
                <Table.Th w={40} />
              </Table.Tr>
            </Table.Thead>
            <Table.Tbody>
              {lines.map((line, index) => (
                <Table.Tr key={line.key}>
                  <Table.Td c="dimmed">{index + 1}</Table.Td>
                  <Table.Td>
                    <TextInput
                      size="xs"
                      miw={160}
                      aria-label={`Item ${index + 1} name`}
                      maxLength={200}
                      value={line.name}
                      onChange={(event) => updateLine(line.key, { name: event.currentTarget.value })}
                    />
                  </Table.Td>
                  <Table.Td>
                    <TextInput
                      size="xs"
                      aria-label={`Item ${index + 1} code`}
                      maxLength={50}
                      value={line.code}
                      onChange={(event) => updateLine(line.key, { code: event.currentTarget.value })}
                    />
                  </Table.Td>
                  <Table.Td>
                    <TextInput
                      size="xs"
                      inputMode="decimal"
                      styles={numberInput}
                      aria-label={`Item ${index + 1} quantity`}
                      value={line.quantity}
                      onChange={(event) => updateLine(line.key, { quantity: event.currentTarget.value })}
                    />
                  </Table.Td>
                  <Table.Td>
                    <TextInput
                      size="xs"
                      inputMode="decimal"
                      styles={numberInput}
                      aria-label={`Item ${index + 1} amount`}
                      value={line.amount}
                      onChange={(event) => updateLine(line.key, { amount: event.currentTarget.value })}
                    />
                  </Table.Td>
                  <Table.Td>
                    <TextInput
                      size="xs"
                      inputMode="decimal"
                      placeholder="0.00"
                      styles={numberInput}
                      aria-label={`Item ${index + 1} discount`}
                      value={line.discount}
                      onChange={(event) => updateLine(line.key, { discount: event.currentTarget.value })}
                    />
                  </Table.Td>
                  <Table.Td>
                    <TextInput
                      size="xs"
                      aria-label={`Item ${index + 1} tax code`}
                      maxLength={16}
                      value={line.taxCode}
                      onChange={(event) => updateLine(line.key, { taxCode: event.currentTarget.value })}
                    />
                  </Table.Td>
                  <Table.Td>
                    <Checkbox
                      aria-label={`Item ${index + 1} is taxed`}
                      checked={line.isTaxed}
                      onChange={(event) => updateLine(line.key, { isTaxed: event.currentTarget.checked })}
                    />
                  </Table.Td>
                  <Table.Td>
                    <ActionIcon
                      variant="subtle"
                      color="red"
                      aria-label={`Remove item ${index + 1}`}
                      onClick={() => setLines((current) => current.filter((other) => other.key !== line.key))}
                    >
                      ✕
                    </ActionIcon>
                  </Table.Td>
                </Table.Tr>
              ))}
            </Table.Tbody>
          </Table>
        </Table.ScrollContainer>

        <Group gap="sm" align="flex-end">
          <Button size="xs" variant="default" onClick={addLine}>
            Add line
          </Button>
          <TextInput
            label="Subtotal"
            size="xs"
            w={110}
            inputMode="decimal"
            styles={numberInput}
            value={subtotal}
            onChange={(event) => setSubtotal(event.currentTarget.value)}
          />
          <TextInput
            label="Tax"
            size="xs"
            w={110}
            inputMode="decimal"
            styles={numberInput}
            value={tax}
            onChange={(event) => setTax(event.currentTarget.value)}
          />
          <TextInput
            label="Total"
            size="xs"
            w={110}
            inputMode="decimal"
            styles={numberInput}
            value={total}
            onChange={(event) => setTotal(event.currentTarget.value)}
          />
        </Group>

        <Text size="sm" c={difference === 0 ? 'green' : 'dimmed'}>
          Lines add up to {formatMoney(linesSum)}
          {difference != null && difference !== 0 && `, which is ${formatMoney(difference)} against the subtotal`}
          {difference === 0 && ', matching the subtotal'}
        </Text>

        <Group gap="xs">
          <Button type="submit" disabled={invalid} loading={saving}>
            Save corrections
          </Button>
          <Button variant="default" onClick={onCancel} disabled={saving}>
            Cancel
          </Button>
        </Group>
      </Stack>
    </form>
  )
}
