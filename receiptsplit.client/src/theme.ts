import { createTheme } from '@mantine/core'

/** Close to the palette the hand-written CSS used, so nothing jumps when a screen is ported. */
export const theme = createTheme({
  primaryColor: 'blue',
  defaultRadius: 'md',
  fontFamily: 'system-ui, "Segoe UI", Roboto, sans-serif',
  headings: { sizes: { h2: { fontSize: '1.05rem', lineHeight: '1.3' } } },
})
