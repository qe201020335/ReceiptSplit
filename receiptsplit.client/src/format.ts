import type { ReceiptStatus } from './api.ts'

export const statusLabels: Record<ReceiptStatus, string> = {
  Queued: 'Queued',
  Processing: 'Reading…',
  Completed: 'Completed',
  NeedsReview: 'Needs review',
  Failed: 'Failed',
}

export function formatMoney(value: number | null | undefined): string {
  return value == null ? '—' : value.toFixed(2)
}

/** 13 percent reads as "13%", Quebec's combined rate as "14.975%". */
export function formatPercent(value: number): string {
  return `${Number(value.toFixed(3))}%`
}

export function formatDateTime(iso: string): string {
  return new Date(iso).toLocaleString(undefined, { dateStyle: 'medium', timeStyle: 'short' })
}

export function formatSeconds(milliseconds: number): string {
  return `${(milliseconds / 1000).toFixed(1)} s`
}
