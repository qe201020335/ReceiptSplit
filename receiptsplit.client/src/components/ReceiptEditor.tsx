import { useState } from 'react'
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
    name: line.name,
    code: line.code ?? '',
    quantity: String(line.quantity),
    amount: line.amount.toFixed(2),
    taxCode: line.taxCode ?? '',
    isTaxed: line.isTaxed,
  }))
}

export function ReceiptEditor({ receipt, onSaved, onCancel }: ReceiptEditorProps) {
  const [storeName, setStoreName] = useState(receipt.storeName ?? '')
  const [purchaseDate, setPurchaseDate] = useState(receipt.purchaseDate ?? '')
  const [subtotal, setSubtotal] = useState(toText(receipt.subtotal))
  const [tax, setTax] = useState(toText(receipt.tax))
  const [total, setTotal] = useState(toText(receipt.total))
  const [lines, setLines] = useState<LineDraft[]>(() => draftLines(receipt))
  const [nextKey, setNextKey] = useState(receipt.lines.length)
  const [saving, setSaving] = useState(false)
  const [error, setError] = useState<string | null>(null)

  function updateLine(key: number, change: Partial<LineDraft>) {
    setLines((current) => current.map((line) => (line.key === key ? { ...line, ...change } : line)))
  }

  function addLine() {
    setLines((current) => [
      ...current,
      { key: nextKey, name: '', code: '', quantity: '1', amount: '', taxCode: '', isTaxed: false },
    ])
    setNextKey((key) => key + 1)
  }

  const amounts = lines.map((line) => toNumber(line.amount))
  const linesSum = amounts.reduce((sum: number, amount) => sum + (amount ?? 0), 0)
  const subtotalValue = toNumber(subtotal)
  const difference = subtotalValue == null ? null : Math.round((linesSum - subtotalValue) * 100) / 100

  const invalid =
    lines.length === 0 ||
    lines.some((line) => line.name.trim() === '' || toNumber(line.amount) == null || toNumber(line.quantity) == null) ||
    [subtotal, tax, total].some((value) => toNumber(value) === undefined)

  async function save() {
    if (invalid) {
      return
    }

    setSaving(true)
    setError(null)
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
            taxCode: line.taxCode.trim() === '' ? null : line.taxCode.trim(),
            isTaxed: line.isTaxed,
          }),
        ),
      }
      onSaved(await api.updateReceipt(receipt.id, edit))
    } catch (e) {
      setError(errorMessage(e))
      setSaving(false)
    }
  }

  return (
    <form
      className="editor"
      onSubmit={(event) => {
        event.preventDefault()
        void save()
      }}
    >
      {error && <p className="error">{error}</p>}

      <div className="editor-fields">
        <label>
          Store
          <input type="text" value={storeName} onChange={(event) => setStoreName(event.target.value)} maxLength={200} />
        </label>
        <label>
          Purchase date
          <input type="date" value={purchaseDate} onChange={(event) => setPurchaseDate(event.target.value)} />
        </label>
      </div>

      <div className="table-scroll">
        <table className="lines editor-lines">
          <thead>
            <tr>
              <th>#</th>
              <th>Item</th>
              <th>Code</th>
              <th className="num">Qty</th>
              <th className="num">Amount</th>
              <th>Tax code</th>
              <th>Taxed</th>
              <th>
                <span className="visually-hidden">Remove</span>
              </th>
            </tr>
          </thead>
          <tbody>
            {lines.map((line, index) => (
              <tr key={line.key}>
                <td className="muted">{index + 1}</td>
                <td>
                  <input
                    type="text"
                    className="name-input"
                    aria-label={`Item ${index + 1} name`}
                    value={line.name}
                    maxLength={200}
                    onChange={(event) => updateLine(line.key, { name: event.target.value })}
                  />
                </td>
                <td>
                  <input
                    type="text"
                    className="code-input"
                    aria-label={`Item ${index + 1} code`}
                    value={line.code}
                    maxLength={50}
                    onChange={(event) => updateLine(line.key, { code: event.target.value })}
                  />
                </td>
                <td>
                  <input
                    type="text"
                    inputMode="decimal"
                    className="qty-input"
                    aria-label={`Item ${index + 1} quantity`}
                    value={line.quantity}
                    onChange={(event) => updateLine(line.key, { quantity: event.target.value })}
                  />
                </td>
                <td>
                  <input
                    type="text"
                    inputMode="decimal"
                    className="amount-input"
                    aria-label={`Item ${index + 1} amount`}
                    value={line.amount}
                    onChange={(event) => updateLine(line.key, { amount: event.target.value })}
                  />
                </td>
                <td>
                  <input
                    type="text"
                    className="code-input"
                    aria-label={`Item ${index + 1} tax code`}
                    value={line.taxCode}
                    maxLength={16}
                    onChange={(event) => updateLine(line.key, { taxCode: event.target.value })}
                  />
                </td>
                <td>
                  <input
                    type="checkbox"
                    aria-label={`Item ${index + 1} is taxed`}
                    checked={line.isTaxed}
                    onChange={(event) => updateLine(line.key, { isTaxed: event.target.checked })}
                  />
                </td>
                <td>
                  <button
                    type="button"
                    className="link"
                    aria-label={`Remove item ${index + 1}`}
                    onClick={() => setLines((current) => current.filter((other) => other.key !== line.key))}
                  >
                    Remove
                  </button>
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>

      <div className="editor-fields">
        <button type="button" onClick={addLine}>
          Add line
        </button>
        <label>
          Subtotal
          <input type="text" inputMode="decimal" value={subtotal} onChange={(event) => setSubtotal(event.target.value)} />
        </label>
        <label>
          Tax
          <input type="text" inputMode="decimal" value={tax} onChange={(event) => setTax(event.target.value)} />
        </label>
        <label>
          Total
          <input type="text" inputMode="decimal" value={total} onChange={(event) => setTotal(event.target.value)} />
        </label>
      </div>

      <p className={difference === 0 ? 'ok' : 'muted'}>
        Lines add up to {formatMoney(linesSum)}
        {difference != null && difference !== 0 && `, which is ${formatMoney(difference)} against the subtotal`}
        {difference === 0 && ', matching the subtotal'}
      </p>

      <div className="actions">
        <button type="submit" className="primary" disabled={invalid || saving}>
          {saving ? 'Saving…' : 'Save corrections'}
        </button>
        <button type="button" onClick={onCancel} disabled={saving}>
          Cancel
        </button>
      </div>
    </form>
  )
}
