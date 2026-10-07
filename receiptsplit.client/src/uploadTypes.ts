// The photo formats the server accepts; keep in step with SupportedFormats in
// src/ReceiptSplit/Extraction/ImagePreparer.cs.

/** 30 MB, matching ReceiptService.MaxUploadBytes, so an oversized photo is refused before it is sent. */
export const maxUploadBytes = 30 * 1024 * 1024

/**
 * MIME types with their file extensions. The extensions matter: outside Apple's systems a HEIC photo often has no
 * MIME type, and the drop zone would refuse it by type alone.
 */
export const acceptedPhotoTypes = {
  'image/jpeg': ['.jpg', '.jpeg'],
  'image/png': ['.png'],
  'image/heic': ['.heic'],
  'image/heif': ['.heif'],
  'image/avif': ['.avif'],
  'image/webp': ['.webp'],
  'image/bmp': ['.bmp'],
  'image/gif': ['.gif'],
  'image/tiff': ['.tif', '.tiff'],
}

export const acceptedPhotoFormats = 'JPEG, PNG, HEIC, WebP, AVIF, GIF, BMP or TIFF'
