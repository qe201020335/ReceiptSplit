// Cropping and rotating a receipt photo in the browser before it is uploaded.

import type { PercentCrop } from 'react-image-crop'

export interface PhotoEdits {
  /** Clockwise quarter turns, 0 to 3, for photos taken sideways or upside down. */
  quarterTurns: number
  /** Degrees clockwise on top of the quarter turns, to level a receipt photographed at a slant. */
  straighten: number
  /** The part to keep, in percent of the rotated photo, so it survives redrawing the preview at another size. */
  crop: PercentCrop
}

export const maxStraighten = 15

export const fullCrop: PercentCrop = { unit: '%', x: 0, y: 0, width: 100, height: 100 }

export const noEdits: PhotoEdits = { quarterTurns: 0, straighten: 0, crop: fullCrop }

/**
 * iOS Safari won't make a canvas larger than about 16.7 million pixels; a bigger photo is scaled down to fit. The
 * server shrinks photos far below this for the model anyway.
 */
const maxOutputPixels = 16_000_000

/** Corners uncovered by straightening are filled like receipt paper rather than left black. */
const fillColor = '#ffffff'

export function isEdited(edits: PhotoEdits): boolean {
  const { crop } = edits
  // The crop tool works in fractional percentages, so a crop dragged back to the edge may not land on exactly 0.
  const cropped = crop.x > 0.01 || crop.y > 0.01 || crop.width < 99.99 || crop.height < 99.99
  return edits.quarterTurns !== 0 || edits.straighten !== 0 || cropped
}

function angle(edits: PhotoEdits): number {
  return ((edits.quarterTurns * 90 + edits.straighten) * Math.PI) / 180
}

/** The size of the box that holds the photo once rotated. */
export function rotatedSize(width: number, height: number, edits: PhotoEdits): { width: number; height: number } {
  const radians = angle(edits)
  const cos = Math.abs(Math.cos(radians))
  const sin = Math.abs(Math.sin(radians))
  return { width: width * cos + height * sin, height: width * sin + height * cos }
}

/**
 * Turns a crop a quarter turn along with the photo. The rotated box turns with it, so percentages map exactly even
 * when the photo is also straightened.
 */
export function turnCrop(crop: PercentCrop, clockwise: boolean): PercentCrop {
  return clockwise
    ? { unit: '%', x: 100 - crop.y - crop.height, y: crop.x, width: crop.height, height: crop.width }
    : { unit: '%', x: crop.y, y: 100 - crop.x - crop.width, width: crop.height, height: crop.width }
}

/** Draws the rotated photo at the given scale, with its box's top left corner at the context's origin. */
function drawRotated(context: CanvasRenderingContext2D, image: HTMLImageElement, edits: PhotoEdits, scale: number) {
  const width = image.naturalWidth
  const height = image.naturalHeight
  const box = rotatedSize(width, height, edits)
  context.scale(scale, scale)
  context.translate(box.width / 2, box.height / 2)
  context.rotate(angle(edits))
  context.drawImage(image, -width / 2, -height / 2, width, height)
}

/** Draws the whole rotated photo, uncropped, for the crop tool to work over. */
export function drawPreview(canvas: HTMLCanvasElement, image: HTMLImageElement, edits: PhotoEdits, scale: number) {
  const box = rotatedSize(image.naturalWidth, image.naturalHeight, edits)
  canvas.width = Math.max(1, Math.round(box.width * scale))
  canvas.height = Math.max(1, Math.round(box.height * scale))
  const context = canvas.getContext('2d')
  if (!context) {
    return
  }
  context.fillStyle = fillColor
  context.fillRect(0, 0, canvas.width, canvas.height)
  context.imageSmoothingQuality = 'high'
  drawRotated(context, image, edits, scale)
}

/**
 * Renders the edited photo at full resolution. A PNG stays a PNG, since those are usually screenshots of e-receipts
 * with sharp text; anything else becomes a JPEG at the quality the server re-encodes at.
 */
export async function renderEdited(image: HTMLImageElement, edits: PhotoEdits, source: File): Promise<File> {
  const box = rotatedSize(image.naturalWidth, image.naturalHeight, edits)
  const { crop } = edits
  const cropWidth = (box.width * crop.width) / 100
  const cropHeight = (box.height * crop.height) / 100
  const scale = Math.min(1, Math.sqrt(maxOutputPixels / (cropWidth * cropHeight)))

  const canvas = document.createElement('canvas')
  canvas.width = Math.max(1, Math.round(cropWidth * scale))
  canvas.height = Math.max(1, Math.round(cropHeight * scale))
  const context = canvas.getContext('2d')
  if (!context) {
    throw new Error("This browser couldn't edit the photo.")
  }
  context.fillStyle = fillColor
  context.fillRect(0, 0, canvas.width, canvas.height)
  context.imageSmoothingQuality = 'high'
  context.translate((-box.width * crop.x * scale) / 100, (-box.height * crop.y * scale) / 100)
  drawRotated(context, image, edits, scale)

  const png = source.type === 'image/png'
  const type = png ? 'image/png' : 'image/jpeg'
  const blob = await new Promise<Blob | null>((resolve) => canvas.toBlob(resolve, type, 0.9))
  if (!blob) {
    throw new Error("This browser couldn't edit the photo.")
  }
  const baseName = source.name.replace(/\.[^.]*$/, '') || 'receipt'
  return new File([blob], `${baseName}.${png ? 'png' : 'jpg'}`, { type })
}

/** Decodes a chosen photo, or returns null for one the browser can't show, such as HEIC outside Safari. */
export async function decodePhoto(url: string): Promise<HTMLImageElement | null> {
  const image = new Image()
  image.src = url
  try {
    await image.decode()
    return image.naturalWidth > 0 ? image : null
  } catch {
    return null
  }
}
