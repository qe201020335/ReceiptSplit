import {
  createTheme,
  defaultVariantColorsResolver,
  type CSSVariablesResolver,
  type VariantColorsResolver,
} from '@mantine/core'

/**
 * Filled primary controls take their text color from --app-primary-contrast, like the old CSS's --accent-text:
 * white on #2f6fed, but near-black on dark mode's light #6b9bff, where white text can't be read.
 */
const variantColorResolver: VariantColorsResolver = (input) => {
  const colors = defaultVariantColorsResolver(input)
  const primary = input.color === undefined || input.color === input.theme.primaryColor
  return input.variant === 'filled' && primary ? { ...colors, color: 'var(--app-primary-contrast)' } : colors
}

/** The palette the hand-written CSS used: #2f6fed in light, lifted to #6b9bff in dark. */
export const theme = createTheme({
  primaryColor: 'brand',
  primaryShade: { light: 6, dark: 4 },
  colors: {
    brand: ['#eef3fe', '#dbe5fb', '#b7c9f7', '#90adf2', '#6b9bff', '#4a81f0', '#2f6fed', '#2a63d4', '#2456bb', '#1d47a0'],
    // Mantine builds dark surfaces from this scale: 4 borders, 5 hover, 6 cards, 7 the page.
    dark: ['#f1f3f5', '#e6e8eb', '#c5cbd3', '#9aa3af', '#39414f', '#2b323d', '#1a1e24', '#111418', '#0c0f13', '#08090b'],
  },
  variantColorResolver,
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
    '--app-primary-contrast': '#ffffff',
    '--app-bg': '#f5f6f8',
    '--mantine-color-body': '#ffffff',
    '--mantine-color-text': '#1d2330',
    '--mantine-color-dimmed': '#667085',
    '--mantine-color-default-border': '#d7dce4',
    // Buttons and inputs sit on a card of the same color, so the border is what separates them.
    '--mantine-color-default': '#ffffff',
    '--mantine-color-default-hover': '#eef0f4',
    '--app-hover': '#eef0f4',
  },
  dark: {
    '--app-primary-contrast': '#0b1020',
    '--app-bg': '#111418',
    '--mantine-color-text': '#e6e8eb',
    '--mantine-color-dimmed': '#9aa3af',
    // Buttons and inputs lift slightly off the card they sit on.
    '--mantine-color-default': '#222831',
    '--mantine-color-default-hover': '#2b323d',
    '--app-hover': '#2b323d',
    // Fainter than an enabled button's #39414f border.
    '--mantine-color-disabled-border': '#2b323d',
  },
})
