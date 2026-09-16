// Sales tax rates, matching the range the backend accepts (ReceiptSplit/Extraction/ReceiptService.cs).

const storageKey = 'receiptsplit.taxRatePercent'

/** Ontario's HST; most receipts here are printed at this rate. */
export const defaultTaxRatePercent = 13

export const minTaxRatePercent = 0

export const maxTaxRatePercent = 30

export function isValidTaxRate(value: number): boolean {
  return Number.isFinite(value) && value >= minTaxRatePercent && value <= maxTaxRatePercent
}

/** The rate used for the last upload in this browser, so a move province is typed once. */
export function lastTaxRatePercent(): number {
  try {
    const stored = Number(window.localStorage.getItem(storageKey))
    return isValidTaxRate(stored) ? stored : defaultTaxRatePercent
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
