/**
 * Copies text to the clipboard. Browsers only offer navigator.clipboard on https and localhost, and the app is
 * usually opened by LAN address over plain http, so fall back to selecting the text in a hidden field and copying.
 */
export async function copyText(text: string): Promise<void> {
  if (window.isSecureContext && navigator.clipboard) {
    await navigator.clipboard.writeText(text)
    return
  }

  const field = document.createElement('textarea')
  field.value = text
  field.readOnly = true
  // Kept on screen (but invisible) so selecting it doesn't scroll the page.
  field.style.position = 'fixed'
  field.style.inset = '0'
  field.style.opacity = '0'
  const focused = document.activeElement
  document.body.append(field)
  field.select()
  try {
    // Deprecated, but it is the only way to copy outside a secure context.
    if (!document.execCommand('copy')) {
      throw new Error('The browser refused to copy. Select the text and copy it by hand.')
    }
  } finally {
    field.remove()
    if (focused instanceof HTMLElement) {
      focused.focus()
    }
  }
}
