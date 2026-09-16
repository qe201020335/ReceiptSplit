import { useRef, useState } from 'react'
import { api, errorMessage, type ReceiptQueued } from '../api.ts'

interface UploadFormProps {
  onUploaded: (receipt: ReceiptQueued) => void
}

export function UploadForm({ onUploaded }: UploadFormProps) {
  const fileInput = useRef<HTMLInputElement>(null)
  const [file, setFile] = useState<File | null>(null)
  const [uploading, setUploading] = useState(false)
  const [error, setError] = useState<string | null>(null)

  async function upload() {
    if (!file) {
      return
    }

    setUploading(true)
    setError(null)
    try {
      const receipt = await api.uploadReceipt(file)
      setFile(null)
      if (fileInput.current) {
        fileInput.current.value = ''
      }
      onUploaded(receipt)
    } catch (e) {
      setError(errorMessage(e))
    } finally {
      setUploading(false)
    }
  }

  return (
    <form
      className="card upload"
      onSubmit={(event) => {
        event.preventDefault()
        void upload()
      }}
    >
      <h2>New receipt</h2>
      <input
        ref={fileInput}
        type="file"
        accept="image/*"
        aria-label="Receipt photo"
        onChange={(event) => setFile(event.target.files?.[0] ?? null)}
      />
      <button type="submit" className="primary" disabled={!file || uploading}>
        {uploading ? 'Uploading…' : 'Upload'}
      </button>
      {error && <p className="error">{error}</p>}
    </form>
  )
}
