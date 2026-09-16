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

export function formatDateTime(iso: string): string {
  return new Date(iso).toLocaleString(undefined, { dateStyle: 'medium', timeStyle: 'short' })
}

export function formatSeconds(milliseconds: number): string {
  return `${(milliseconds / 1000).toFixed(1)} s`
}
