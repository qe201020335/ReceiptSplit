// Sales tax rates, matching the range the backend accepts (src/ReceiptSplit/Extraction/ReceiptService.cs).

const storageKey = 'receiptsplit.taxRatePercent'

/** Ontario's HST; most receipts here are printed at this rate. */
export const defaultTaxRatePercent = 13

export const minTaxRatePercent = 0

export const maxTaxRatePercent = 30

export function isValidTaxRate(value: number): boolean {
  return Number.isFinite(value) && value >= minTaxRatePercent && value <= maxTaxRatePercent
}

/** The rate used for the last upload in this browser, so another province's rate is typed once. */
export function lastTaxRatePercent(): number {
  try {
    // Nothing stored reads as Number(null) === 0, which is itself a valid rate, so check for the key first.
    const stored = window.localStorage.getItem(storageKey)
    const rate = stored === null ? Number.NaN : Number(stored)
    return isValidTaxRate(rate) ? rate : defaultTaxRatePercent
  } catch {
    return defaultTaxRatePercent
  }
}

export function rememberTaxRatePercent(value: number): void {
  try {
    window.localStorage.setItem(storageKey, String(value))
  } catch {
    // Private windows and blocked site data are fine; the default applies next time.
  }
}
