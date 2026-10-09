import type { ReactNode } from 'react'
import { Skeleton, VisuallyHidden, type SkeletonProps } from '@mantine/core'
import { useReducedMotion } from '@mantine/hooks'
import classes from './Placeholder.module.css'

interface LoadingProps {
  /** What screen readers hear while it loads, such as "Loading receipts". */
  label: string
  className?: string
  children: ReactNode
}

/**
 * Wraps whatever stands in for content while its first data loads: busy for assistive technology, which hears the
 * label, while sighted people see the placeholders or spinner inside.
 */
export function Loading({ label, className, children }: LoadingProps) {
  return (
    <div aria-busy="true" className={className}>
      <VisuallyHidden>{label}</VisuallyHidden>
      <div aria-hidden>{children}</div>
    </div>
  )
}

/** A skeleton shape. Mantine's pulse ignores prefers-reduced-motion, so it's turned off here when that's asked for. */
export function Bar(props: SkeletonProps) {
  const reducedMotion = useReducedMotion()
  return <Skeleton animate={!reducedMotion} {...props} />
}

/** Fixed rather than random, so rows differ from each other but render the same every time. */
const titleWidths = ['58%', '42%', '66%', '50%', '38%', '61%', '46%']
const ownerWidths = [96, 72, 84, 64, 90, 78, 70]

interface ReceiptRowsPlaceholderProps {
  rows: number
  /** The start page's list, or the receipt manager's rows with their checkbox and buttons. */
  variant: 'list' | 'manager'
  /** An admin's manager rows also have an owner line and Reassign below Open. */
  withOwner?: boolean
}

/** Rows shaped like receipt summaries: the store and total, then the date and status, as the real rows lay them out. */
export function ReceiptRowsPlaceholder({ rows, variant, withOwner = false }: ReceiptRowsPlaceholderProps) {
  return <>{Array.from({ length: rows }, (_, i) => row(i))}</>

  function row(i: number) {
    const lines = (
      <div className={classes.lines}>
        <div className={classes.titleLine}>
          <Bar height={14} width={titleWidths[i % titleWidths.length]} />
          <Bar height={14} width={48} />
        </div>
        <div className={variant === 'list' ? classes.dateLineSmall : classes.dateLine}>
          <Bar height={10} width={76} />
          <Bar height={18} width={78} radius="xl" />
        </div>
      </div>
    )
    if (variant === 'list') {
      return (
        <div key={i} className={classes.listRow}>
          {lines}
        </div>
      )
    }
    return (
      <div key={i} className={classes.managerRow} data-with-owner={withOwner || undefined}>
        <div className={classes.managerMain}>
          <div className={classes.toggle}>
            <Bar height={20} width={20} radius="sm" className={classes.checkbox} />
            {lines}
          </div>
          {withOwner && (
            <div className={classes.owner}>
              <Bar height={22} circle />
              <Bar height={10} width={ownerWidths[i % ownerWidths.length]} />
            </div>
          )}
        </div>
        <div className={classes.actions}>
          <Bar height={30} width={withOwner ? 76 : 58} />
          {withOwner && <Bar height={30} width={76} />}
        </div>
      </div>
    )
  }
}
