// Types and calls for the ReceiptSplit backend (src/ReceiptSplit/Contracts/ReceiptDtos.cs).

export type ReceiptStatus = 'Queued' | 'Processing' | 'Completed' | 'NeedsReview' | 'Failed'

export interface ReceiptQueued {
  id: string
  status: ReceiptStatus
}

export interface ReceiptSummary {
  id: string
  createdAt: string
  status: ReceiptStatus
  storeName: string | null
  purchaseDate: string | null
  total: number | null
}

export interface ReceiptLine {
  position: number
  name: string
  code: string | null
  quantity: number
  amount: number
  /** Promotion already deducted from the amount, negative, or 0; the printed price is amount - discount. */
  discount: number
  taxCode: string | null
  isTaxed: boolean
}

export interface ReceiptChecks {
  linesSum: number
  linesMatchSubtotal: boolean
  /** Storewide discount taken off after the subtotal, negative or 0. */
  discount: number
  totalMatches: boolean
  taxedSum: number
  expectedTax: number
  taxMatches: boolean
}

export interface Extraction {
  extractedAt: string
  model: string | null
  promptTokens: number | null
  completionTokens: number | null
  durationMs: number | null
  sentImageWidth: number | null
  sentImageHeight: number | null
  modelOutput: string | null
}

/** The editable part of a receipt: what a person can correct when the model misreads the photo. */
export interface ReceiptEdit {
  storeName: string | null
  purchaseDate: string | null
  subtotal: number | null
  discountPercent: number
  tax: number | null
  total: number | null
  lines: ReceiptLineEdit[]
}

export interface ReceiptLineEdit {
  name: string
  code: string | null
  quantity: number
  amount: number
  discount: number
  taxCode: string | null
  isTaxed: boolean
}

export interface ReceiptDetail {
  id: string
  createdAt: string
  originalFileName: string
  status: ReceiptStatus
  error: string | null
  storeName: string | null
  purchaseDate: string | null
  editedAt: string | null
  taxRatePercent: number
  subtotal: number | null
  /** Percentage taken off the whole purchase after the subtotal; 0 for none. */
  discountPercent: number
  tax: number | null
  /** Entered at upload: the prices already include the tax, so the tax is ignored everywhere. */
  taxIncluded: boolean
  total: number | null
  checks: ReceiptChecks | null
  lines: ReceiptLine[]
  extraction: Extraction | null
}

export class ApiError extends Error {
  readonly status: number

  constructor(status: number, message: string) {
    super(message)
    this.name = 'ApiError'
    this.status = status
  }
}

/** Queued and Processing receipts are still waiting on the model. */
export function inProgress(status: ReceiptStatus): boolean {
  return status === 'Queued' || status === 'Processing'
}

export function errorMessage(error: unknown): string {
  return error instanceof Error ? error.message : String(error)
}

async function request<T>(path: string, init?: RequestInit): Promise<T> {
  const response = await fetch(path, init)
  if (!response.ok) {
    throw new ApiError(response.status, await problemMessage(response))
  }

  return response.status === 204 ? (undefined as T) : ((await response.json()) as T)
}

/** Reads an ASP.NET ProblemDetails body, falling back to the HTTP status. */
async function problemMessage(response: Response): Promise<string> {
  try {
    const problem = (await response.json()) as { title?: string; detail?: string }
    return problem.detail ?? problem.title ?? `${response.status} ${response.statusText}`
  } catch {
    return `${response.status} ${response.statusText}`
  }
}

export const api = {
  listReceipts: () => request<ReceiptSummary[]>('/api/receipts'),

  getReceipt: (id: string) => request<ReceiptDetail>(`/api/receipts/${id}`),

  uploadReceipt: (file: File, taxRatePercent: number, taxIncluded: boolean) => {
    const form = new FormData()
    form.append('file', file)
    form.append('taxRatePercent', String(taxRatePercent))
    form.append('taxIncluded', String(taxIncluded))
    return request<ReceiptQueued>('/api/receipts', { method: 'POST', body: form })
  },

  /** Replaces the extracted lines and totals with corrected ones; no new extraction. */
  updateReceipt: (id: string, edit: ReceiptEdit) =>
    request<ReceiptDetail>(`/api/receipts/${id}`, {
      method: 'PUT',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(edit),
    }),

  /** Re-checks the receipt against a different sales tax rate, or with tax included or not; no new extraction. */
  updateTaxRate: (id: string, taxRatePercent: number, taxIncluded: boolean) =>
    request<ReceiptDetail>(`/api/receipts/${id}`, {
      method: 'PATCH',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ taxRatePercent, taxIncluded }),
    }),

  rerunExtraction: (id: string) => request<ReceiptQueued>(`/api/receipts/${id}/extract`, { method: 'POST' }),

  deleteReceipt: (id: string) => request<void>(`/api/receipts/${id}`, { method: 'DELETE' }),

  /**
   * Deletes several receipts, or none: an unknown or someone else's receipt fails it with 404, and one being read
   * with 409. The error's message then says why nothing was deleted.
   */
  deleteReceipts: (ids: string[]) =>
    request<void>('/api/receipts/delete', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ ids }),
    }),

  imageUrl: (id: string) => `/api/receipts/${id}/image`,
}
