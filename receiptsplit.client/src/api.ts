// Types and calls for the ReceiptSplit backend (src/ReceiptSplit/Contracts/ReceiptDtos.cs and UserDtos.cs).

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

/** The signed-in user. */
export interface Account {
  id: string
  email: string | null
  name: string | null
  isAdmin: boolean
  /** Where signing out goes; null when there is nothing to sign out of, as in development. */
  signOutUrl: string | null
}

/** The codes the server's sign-in refusals carry (SignInProblem.cs). */
const signInRefusalCodes = ['unsupported-sign-in', 'account-conflict', 'identity-unavailable'] as const

/**
 * Why the sign-in can't be used; the app shows a page for each instead of the receipts. Derived from the refusal
 * codes, so a code added there needs its page in SignInProblemPage before the build passes.
 */
export type SignInProblemCode = (typeof signInRefusalCodes)[number] | 'signed-out'

function isSignInRefusal(code: string | undefined): code is (typeof signInRefusalCodes)[number] {
  return (signInRefusalCodes as readonly (string | undefined)[]).includes(code)
}

export interface SignInProblem {
  code: SignInProblemCode
  signOutUrl: string | null
}

const signInProblemListeners = new Set<(problem: SignInProblem) => void>()

/** Calls the listener whenever a request shows the sign-in can't be used; returns a function that unsubscribes. */
export function onSignInProblem(listener: (problem: SignInProblem) => void): () => void {
  signInProblemListeners.add(listener)
  return () => {
    signInProblemListeners.delete(listener)
  }
}

function reportSignInProblem(problem: SignInProblem) {
  for (const listener of signInProblemListeners) {
    listener(problem)
  }
}

export class ApiError extends Error {
  readonly status: number

  /** The problem's code, when the server gave one. */
  readonly code: string | null

  constructor(status: number, message: string, code: string | null = null) {
    super(message)
    this.name = 'ApiError'
    this.status = status
    this.code = code
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
  // The API never redirects. A redirect means the proxy in front of the app is sending the browser to sign in again,
  // which a fetch can't follow, so it's reported as signed out instead of failing on the sign-in page's HTML.
  const response = await fetch(path, { ...init, redirect: 'manual' })
  if (response.type === 'opaqueredirect') {
    reportSignInProblem({ code: 'signed-out', signOutUrl: null })
    throw new ApiError(response.status, 'You were signed out.', 'signed-out')
  }

  if (!response.ok) {
    const problem = await readProblem(response)
    if (isSignInRefusal(problem.code)) {
      reportSignInProblem({ code: problem.code, signOutUrl: problem.signOutUrl ?? null })
    }
    throw new ApiError(
      response.status,
      problem.detail ?? problem.title ?? `${response.status} ${response.statusText}`,
      problem.code ?? null,
    )
  }

  return response.status === 204 ? (undefined as T) : ((await response.json()) as T)
}

interface Problem {
  title?: string
  detail?: string
  code?: string
  signOutUrl?: string | null
}

/** Reads an ASP.NET ProblemDetails body; empty when the response isn't one. */
async function readProblem(response: Response): Promise<Problem> {
  try {
    return (await response.json()) as Problem
  } catch {
    return {}
  }
}

export const api = {
  me: () => request<Account>('/api/me'),

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
