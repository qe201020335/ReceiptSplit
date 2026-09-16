import { useEffect, useState } from 'react'
import { api, errorMessage, inProgress, type ReceiptDetail as Receipt } from '../api.ts'
import { formatDateTime, formatMoney, formatPercent, formatSeconds } from '../format.ts'
import { isValidTaxRate, maxTaxRatePercent, minTaxRatePercent } from '../taxRate.ts'
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
  const [taxRateDraft, setTaxRateDraft] = useState<string | null>(null)
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

  async function saveTaxRate(draft: string) {
    const rate = Number(draft)
    if (draft.trim() === '' || !isValidTaxRate(rate)) {
      return
    }

    setBusy(true)
    setError(null)
    try {
      const updated = await api.updateTaxRate(id, rate)
      setReceipt(updated)
      setTaxRateDraft(null)
      onChanged()
    } catch (e) {
      setError(errorMessage(e))
    } finally {
      setBusy(false)
    }
  }

  async function rerun() {
    setBusy(true)
    setError(null)
    try {
      await api.rerunExtraction(id)
      setVersion((v) => v + 1)
      onChanged()
    } catch (e) {
      setError(errorMessage(e))
    } finally {
      setBusy(false)
    }
  }

  async function remove() {
    if (!window.confirm('Delete this receipt and its photo?')) {
      return
    }
    setBusy(true)
    try {
      await api.deleteReceipt(id)
      onDeleted()
    } catch (e) {
      setError(errorMessage(e))
      setBusy(false)
    }
  }

  if (!receipt) {
    return <div className="card">{error ? <p className="error">{error}</p> : <p className="muted">Loading…</p>}</div>
  }

  const { checks, extraction } = receipt

  return (
    <article className="card receipt-detail">
      <header className="detail-header">
        <div>
          <h2>{receipt.storeName ?? 'Unknown store'}</h2>
          <p className="muted">
            {receipt.purchaseDate ?? 'No purchase date'} · uploaded {formatDateTime(receipt.createdAt)} ·{' '}
            {receipt.originalFileName}
          </p>
          {taxRateDraft === null ? (
            <p className="tax-rate">
              Sales tax {formatPercent(receipt.taxRatePercent)}
              <button
                type="button"
                className="link"
                onClick={() => setTaxRateDraft(String(receipt.taxRatePercent))}
                disabled={busy || receipt.status === 'Processing'}
              >
                Change
              </button>
            </p>
          ) : (
            <form
              className="tax-rate"
              onSubmit={(event) => {
                event.preventDefault()
                void saveTaxRate(taxRateDraft)
              }}
            >
              <label className="tax-rate-field">
                Sales tax
                <input
                  type="number"
                  inputMode="decimal"
                  autoFocus
                  min={minTaxRatePercent}
                  max={maxTaxRatePercent}
                  step={0.001}
                  value={taxRateDraft}
                  onChange={(event) => setTaxRateDraft(event.target.value)}
                />
                %
              </label>
              <button type="submit" disabled={busy || taxRateDraft.trim() === '' || !isValidTaxRate(Number(taxRateDraft))}>
                Save
              </button>
              <button type="button" onClick={() => setTaxRateDraft(null)} disabled={busy}>
                Cancel
              </button>
            </form>
          )}
        </div>
        <StatusBadge status={receipt.status} />
      </header>

      {error && <p className="error">{error}</p>}
      {waiting && (
        <p className="notice">
          Reading the receipt. This usually takes about 20 seconds, or up to a minute if the model has to load first.
        </p>
      )}
      {receipt.error && <p className="error">{receipt.error}</p>}
      {receipt.status === 'NeedsReview' && !receipt.error && (
        <p className="warning">
          The lines don't agree with the receipt's printed totals. Check them against the photo, and check that the sales tax
          rate is right for where you shopped.
        </p>
      )}

      {receipt.lines.length > 0 && (
        <div className="table-scroll">
          <table className="lines">
            <thead>
              <tr>
                <th>#</th>
                <th>Item</th>
                <th className="num">Qty</th>
                <th className="num">Amount</th>
                <th>Tax</th>
              </tr>
            </thead>
            <tbody>
              {receipt.lines.map((line) => (
                <tr key={line.position}>
                  <td className="muted">{line.position + 1}</td>
                  <td>
                    {line.name}
                    {line.code && <span className="muted item-code"> {line.code}</span>}
                  </td>
                  <td className="num">{line.quantity}</td>
                  <td className="num">{formatMoney(line.amount)}</td>
                  <td className="tax">
                    {line.taxCode}
                    {line.isTaxed && <span className="taxed" title={`Taxed at ${formatPercent(receipt.taxRatePercent)}`}> ✓</span>}
                  </td>
                </tr>
              ))}
            </tbody>
            <tfoot>
              <tr>
                <th colSpan={3}>Subtotal</th>
                <td className="num">{formatMoney(receipt.subtotal)}</td>
                <td />
              </tr>
              {checks && (
                <tr>
                  <th colSpan={3}>Taxed items</th>
                  <td className="num">{formatMoney(checks.taxedSum)}</td>
                  <td />
                </tr>
              )}
              <tr>
                <th colSpan={3}>Tax</th>
                <td className="num">{formatMoney(receipt.tax)}</td>
                <td />
              </tr>
              <tr className="total">
                <th colSpan={3}>Total</th>
                <td className="num">{formatMoney(receipt.total)}</td>
                <td />
              </tr>
            </tfoot>
          </table>
        </div>
      )}

      {checks && (
        <ul className="checks">
          <li className={checks.linesMatchSubtotal ? 'ok' : 'bad'}>
            {checks.linesMatchSubtotal
              ? `✓ Lines add up to the subtotal (${formatMoney(checks.linesSum)})`
              : `✗ Lines add up to ${formatMoney(checks.linesSum)}, but the subtotal is ${formatMoney(receipt.subtotal)}`}
          </li>
          <li className={checks.totalMatches ? 'ok' : 'bad'}>
            {checks.totalMatches ? '✓ Subtotal + tax equals the total' : "✗ Subtotal + tax doesn't equal the total"}
          </li>
          <li className={checks.taxMatches ? 'ok' : 'bad'}>
            {checks.taxMatches ? '✓' : '✗'} {formatPercent(receipt.taxRatePercent)} tax on {formatMoney(checks.taxedSum)} of
            taxed items is {formatMoney(checks.expectedTax)}
            {checks.taxMatches ? '' : `, but the receipt shows ${formatMoney(receipt.tax)}`}
          </li>
        </ul>
      )}

      <div className="actions">
        <button type="button" onClick={() => void rerun()} disabled={busy || waiting}>
          Re-run extraction
        </button>
        <button type="button" className="danger" onClick={() => void remove()} disabled={busy || receipt.status === 'Processing'}>
          Delete
        </button>
      </div>

      <details className="photo">
        <summary>Original photo</summary>
        <a href={api.imageUrl(receipt.id)} target="_blank" rel="noreferrer">
          <img src={api.imageUrl(receipt.id)} alt={`Uploaded photo ${receipt.originalFileName}`} loading="lazy" />
        </a>
      </details>

      {extraction && (
        <details className="diagnostics">
          <summary>Extraction details</summary>
          <p className="muted">
            {extraction.model ?? 'Unknown model'}
            {extraction.durationMs != null && ` · ${formatSeconds(extraction.durationMs)}`}
            {extraction.promptTokens != null && ` · ${extraction.promptTokens} prompt + ${extraction.completionTokens} output tokens`}
            {extraction.sentImageWidth != null && ` · image sent at ${extraction.sentImageWidth}×${extraction.sentImageHeight}`}
          </p>
          {extraction.modelOutput && <pre>{extraction.modelOutput}</pre>}
        </details>
      )}
    </article>
  )
}
