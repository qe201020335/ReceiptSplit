import { createTheme, type CSSVariablesResolver } from '@mantine/core'

/** The palette the hand-written CSS used: #2f6fed in light, lifted to #6b9bff in dark. */
export const theme = createTheme({
  primaryColor: 'brand',
  primaryShade: { light: 6, dark: 4 },
  colors: {
    brand: ['#eef3fe', '#dbe5fb', '#b7c9f7', '#90adf2', '#6b9bff', '#4a81f0', '#2f6fed', '#2a63d4', '#2456bb', '#1d47a0'],
  },
  defaultRadius: 'md',
  fontFamily: 'system-ui, "Segoe UI", Roboto, sans-serif',
  headings: { sizes: { h2: { fontSize: '1.05rem', lineHeight: '1.3' } } },
})

/**
 * Mantine paints the page and its surfaces the same color; the old stylesheet used a tinted page
 * behind white cards, so the page background gets its own variable.
 */
export const cssVariablesResolver: CSSVariablesResolver = () => ({
  variables: {},
  light: {
    '--app-bg': '#f5f6f8',
    '--mantine-color-body': '#ffffff',
    '--mantine-color-text': '#1d2330',
    '--mantine-color-dimmed': '#667085',
    '--mantine-color-default-border': '#e2e5ea',
  },
  dark: {
    '--app-bg': '#111418',
    '--mantine-color-body': '#1a1e24',
    '--mantine-color-text': '#e6e8eb',
    '--mantine-color-dimmed': '#9aa3af',
    '--mantine-color-default-border': '#2b313a',
  },
})
