// Splitting a receipt between people. Everything is in whole cents so the parts always add back up.

import type { ReceiptDetail } from './api.ts'

/** A receipt line with its share of the tax included, which is what people pay for it. */
export interface SplitLine {
  position: number
  name: string
  code: string | null
  quantity: number
  /** Promotion already deducted, negative, or 0. */
  discount: number
  isTaxed: boolean
  cents: number
}

export type SplitMode = 'shares' | 'amounts'

/**
 * Who pays for one line. Shares and amounts are both kept so switching modes back and forth loses neither;
 * only the one for the current mode counts.
 */
export interface LineAssignment {
  mode: SplitMode
  /** Person -> number of equal shares. */
  shares: Record<string, number>
  /** Person -> cents, tax included. */
  amounts: Record<string, number>
}

export interface SplitState {
  people: string[]
  /** Keyed by line position; a missing line is split by shares with nobody assigned. */
  lines: Record<number, LineAssignment>
}

export type LineProblem = 'unassigned' | 'mismatch'

export interface PersonPart {
  person: string
  cents: number
  /** How many of the line's shares are this person's, when split by shares. */
  shares?: number
}

export interface LineSplit {
  parts: PersonPart[]
  /** Total number of shares, when split by shares. */
  totalShares?: number
  assignedCents: number
  problem: LineProblem | null
}

export interface PersonItem {
  line: SplitLine
  part: PersonPart
  split: LineSplit
}

export interface PersonTotal {
  person: string
  cents: number
  items: PersonItem[]
}

export interface SplitSummary {
  people: PersonTotal[]
  assignedCents: number
  /** Sum of every line, tax included. */
  linesCents: number
  receiptTotalCents: number | null
  unassigned: number
  mismatched: number
}

export const emptySplitState: SplitState = { people: [], lines: {} }

const emptyAssignment: LineAssignment = { mode: 'shares', shares: {}, amounts: {} }

export function toCents(value: number): number {
  return Math.round(value * 100)
}

export function formatCents(cents: number): string {
  return (cents / 100).toFixed(2)
}

/**
 * Divides total cents in proportion to the weights, whole cents only, handing the cents that rounding down leaves
 * over to the largest remainders (the earlier entry on a tie). The weights must add up to more than zero.
 */
export function allocate(total: number, weights: number[]): number[] {
  const weightSum = weights.reduce((sum, weight) => sum + weight, 0)
  const parts = weights.map((weight) => Math.floor((total * weight) / weightSum))
  const remainders = weights.map((weight, index) => total * weight - parts[index] * weightSum)
  let left = total - parts.reduce((sum, part) => sum + part, 0)
  const order = weights.map((_, index) => index).sort((a, b) => remainders[b] - remainders[a] || a - b)
  for (const index of order) {
    if (left <= 0) {
      break
    }
    parts[index] += 1
    left -= 1
  }
  return parts
}

/**
 * The receipt's lines with tax included. The printed tax is shared out over the taxed lines in proportion to their
 * amounts, so the lines add up to exactly the printed total rather than drifting a cent per line.
 */
export function splitLines(receipt: ReceiptDetail): SplitLine[] {
  const base = receipt.lines.map((line) => toCents(line.amount))
  const taxed = receipt.lines.map((line, index) => (line.isTaxed ? base[index] : 0))
  const taxedCents = taxed.reduce((sum, cents) => sum + cents, 0)
  const tax =
    receipt.tax != null && taxedCents > 0
      ? allocate(toCents(receipt.tax), taxed)
      : taxed.map((cents) => Math.round((cents * receipt.taxRatePercent) / 100))

  return receipt.lines.map((line, index) => ({
    position: line.position,
    name: line.name,
    code: line.code,
    quantity: line.quantity,
    discount: line.discount,
    isTaxed: line.isTaxed,
    cents: base[index] + tax[index],
  }))
}

export function assignmentFor(state: SplitState, position: number): LineAssignment {
  return state.lines[position] ?? emptyAssignment
}

export function lineSplit(line: SplitLine, assignment: LineAssignment, people: string[]): LineSplit {
  if (assignment.mode === 'amounts') {
    const parts = people
      .filter((person) => assignment.amounts[person] != null)
      .map((person) => ({ person, cents: assignment.amounts[person] }))
    const assignedCents = parts.reduce((sum, part) => sum + part.cents, 0)
    const problem = parts.length === 0 ? 'unassigned' : assignedCents !== line.cents ? 'mismatch' : null
    return { parts, assignedCents, problem }
  }

  const holders = people.filter((person) => (assignment.shares[person] ?? 0) > 0)
  if (holders.length === 0) {
    return { parts: [], totalShares: 0, assignedCents: 0, problem: 'unassigned' }
  }

  const counts = holders.map((person) => assignment.shares[person])
  const cents = allocate(line.cents, counts)
  return {
    parts: holders.map((person, index) => ({ person, cents: cents[index], shares: counts[index] })),
    totalShares: counts.reduce((sum, count) => sum + count, 0),
    assignedCents: line.cents,
    problem: null,
  }
}

export function summarize(lines: SplitLine[], state: SplitState, receiptTotal: number | null): SplitSummary {
  const people = state.people.map((person): PersonTotal => ({ person, cents: 0, items: [] }))
  let assignedCents = 0
  let unassigned = 0
  let mismatched = 0

  for (const line of lines) {
    const split = lineSplit(line, assignmentFor(state, line.position), state.people)
    assignedCents += split.assignedCents
    if (split.problem === 'unassigned') {
      unassigned += 1
    } else if (split.problem === 'mismatch') {
      mismatched += 1
    }
    for (const part of split.parts) {
      const total = people.find((person) => person.person === part.person)
      if (total) {
        total.cents += part.cents
        total.items.push({ line, part, split })
      }
    }
  }

  return {
    people,
    assignedCents,
    linesCents: lines.reduce((sum, line) => sum + line.cents, 0),
    receiptTotalCents: receiptTotal == null ? null : toCents(receiptTotal),
    unassigned,
    mismatched,
  }
}

/** Plain text for pasting into a chat: each person's total, then what it is made of. */
export function summaryText(summary: SplitSummary): string {
  return summary.people
    .map(({ person, cents, items }) =>
      [
        `${person}: ${formatCents(cents)}`,
        ...items.map(({ line, part, split }) => {
          const note =
            split.totalShares == null
              ? ` (of ${formatCents(line.cents)})`
              : split.totalShares > 1
                ? ` (${part.shares}/${split.totalShares} share)`
                : ''
          return `  ${line.name}: ${formatCents(part.cents)}${note}`
        }),
      ].join('\n'),
    )
    .join('\n\n')
}

export function hasAssignments(state: SplitState, person: string): boolean {
  return Object.values(state.lines).some(
    (assignment) => (assignment.shares[person] ?? 0) > 0 || assignment.amounts[person] != null,
  )
}

function withoutKey<T>(record: Record<string, T>, key: string): Record<string, T> {
  const { [key]: _removed, ...rest } = record
  return rest
}

function updateLine(
  state: SplitState,
  position: number,
  change: (assignment: LineAssignment) => LineAssignment,
): SplitState {
  return { ...state, lines: { ...state.lines, [position]: change(assignmentFor(state, position)) } }
}

/** Sets the list of names, trimmed and without repeats; anyone dropped loses their assignments. */
export function setPeople(state: SplitState, names: string[]): SplitState {
  const people = [...new Set(names.map((name) => name.trim()).filter((name) => name !== ''))]
  const removed = state.people.filter((person) => !people.includes(person))
  const lines = Object.fromEntries(
    Object.entries(state.lines).map(([position, assignment]) => [
      position,
      removed.reduce(
        (current, person) => ({
          ...current,
          shares: withoutKey(current.shares, person),
          amounts: withoutKey(current.amounts, person),
        }),
        assignment,
      ),
    ]),
  )
  return { people, lines }
}

export function changeShare(state: SplitState, position: number, person: string, delta: number): SplitState {
  return updateLine(state, position, (assignment) => {
    const count = (assignment.shares[person] ?? 0) + delta
    return {
      ...assignment,
      shares: count > 0 ? { ...assignment.shares, [person]: count } : withoutKey(assignment.shares, person),
    }
  })
}

export function addEveryone(state: SplitState, position: number): SplitState {
  return updateLine(state, position, (assignment) => ({
    ...assignment,
    shares: Object.fromEntries(state.people.map((person) => [person, (assignment.shares[person] ?? 0) + 1])),
  }))
}

/** Clears whichever of shares or amounts the line is currently split by. */
export function clearLine(state: SplitState, position: number): SplitState {
  return updateLine(state, position, (assignment) =>
    assignment.mode === 'amounts' ? { ...assignment, amounts: {} } : { ...assignment, shares: {} },
  )
}

export function setMode(state: SplitState, position: number, mode: SplitMode): SplitState {
  return updateLine(state, position, (assignment) => ({ ...assignment, mode }))
}

/** Sets a person's amount in cents for a line, or removes it when null. */
export function setAmount(state: SplitState, position: number, person: string, cents: number | null): SplitState {
  return updateLine(state, position, (assignment) => ({
    ...assignment,
    amounts: cents == null ? withoutKey(assignment.amounts, person) : { ...assignment.amounts, [person]: cents },
  }))
}
