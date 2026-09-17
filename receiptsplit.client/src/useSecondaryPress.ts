import { useEffect, useMemo, useRef, type KeyboardEvent, type MouseEvent, type PointerEvent } from 'react'

const holdMs = 450
/** A finger that moves further than this is scrolling, not holding. */
const moveTolerancePx = 10

/**
 * Handlers for a button's second action: right click with a mouse, a long press on a touch screen, or Delete /
 * Backspace from the keyboard. Browsers have no long-press event: Android Chrome turns one into a contextmenu event
 * and iOS Safari fires nothing, so the hold is timed here, and whichever of the two comes first wins. The click the
 * browser still sends when the finger lifts is swallowed, so a long press doesn't also count as a tap.
 */
export function useSecondaryPress(onSecondary: () => void) {
  const callback = useRef(onSecondary)
  const timer = useRef<number | undefined>(undefined)
  const start = useRef<{ x: number; y: number } | null>(null)
  const touching = useRef(false)
  const fired = useRef(false)

  useEffect(() => {
    callback.current = onSecondary
  })
  useEffect(() => () => window.clearTimeout(timer.current), [])

  return useMemo(() => {
    function stopTiming() {
      window.clearTimeout(timer.current)
      timer.current = undefined
      start.current = null
    }

    function fire() {
      stopTiming()
      fired.current = true
      navigator.vibrate?.(15)
      callback.current()
    }

    return {
      onPointerDown: (event: PointerEvent) => {
        fired.current = false
        touching.current = event.pointerType !== 'mouse'
        if (!touching.current) {
          return
        }
        start.current = { x: event.clientX, y: event.clientY }
        timer.current = window.setTimeout(fire, holdMs)
      },
      onPointerMove: (event: PointerEvent) => {
        const from = start.current
        if (from && Math.hypot(event.clientX - from.x, event.clientY - from.y) > moveTolerancePx) {
          stopTiming()
        }
      },
      onPointerUp: stopTiming,
      onPointerCancel: stopTiming,
      onContextMenu: (event: MouseEvent) => {
        event.preventDefault()
        // A touch hold: Android's own contextmenu may beat the timer or follow it; act on it only once.
        if (touching.current) {
          if (!fired.current) {
            fire()
          }
          return
        }
        callback.current()
      },
      onClickCapture: (event: MouseEvent) => {
        if (fired.current) {
          fired.current = false
          event.preventDefault()
          event.stopPropagation()
        }
      },
      onKeyDown: (event: KeyboardEvent) => {
        if (event.key === 'Delete' || event.key === 'Backspace') {
          event.preventDefault()
          callback.current()
        }
      },
    }
  }, [])
}
