import { useRef, useState } from 'react'
import { api, errorMessage, type ReceiptQueued } from '../api.ts'
import { isValidTaxRate, lastTaxRatePercent, maxTaxRatePercent, minTaxRatePercent, rememberTaxRatePercent } from '../taxRate.ts'

interface UploadFormProps {
  onUploaded: (receipt: ReceiptQueued) => void
}

export function UploadForm({ onUploaded }: UploadFormProps) {
  const fileInput = useRef<HTMLInputElement>(null)
  const [file, setFile] = useState<File | null>(null)
  // Kept as text so a half-typed rate such as "12." doesn't fight the input.
  const [taxRate, setTaxRate] = useState(() => String(lastTaxRatePercent()))
  const [uploading, setUploading] = useState(false)
  const [error, setError] = useState<string | null>(null)

  const rate = Number(taxRate)
  const rateValid = taxRate.trim() !== '' && isValidTaxRate(rate)

  async function upload() {
    if (!file || !rateValid) {
      return
    }

    setUploading(true)
    setError(null)
    try {
      const receipt = await api.uploadReceipt(file, rate)
      rememberTaxRatePercent(rate)
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
      <label className="tax-rate-field">
        Sales tax
        <input
          type="number"
          inputMode="decimal"
          min={minTaxRatePercent}
          max={maxTaxRatePercent}
          step={0.001}
          value={taxRate}
          onChange={(event) => setTaxRate(event.target.value)}
        />
        %
      </label>
      <button type="submit" className="primary" disabled={!file || !rateValid || uploading}>
        {uploading ? 'Uploading…' : 'Upload'}
      </button>
      {!rateValid && <p className="muted">Enter a rate between {minTaxRatePercent} and {maxTaxRatePercent}%.</p>}
      {error && <p className="error">{error}</p>}
    </form>
  )
}
