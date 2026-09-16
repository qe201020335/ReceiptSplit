import { useState } from 'react'
import { Alert, Button, Card, FileInput, NumberInput, Stack, Title } from '@mantine/core'
import { api, errorMessage, type ReceiptQueued } from '../api.ts'
import { isValidTaxRate, lastTaxRatePercent, maxTaxRatePercent, minTaxRatePercent, rememberTaxRatePercent } from '../taxRate.ts'

interface UploadFormProps {
  onUploaded: (receipt: ReceiptQueued) => void
}

export function UploadForm({ onUploaded }: UploadFormProps) {
  const [file, setFile] = useState<File | null>(null)
  const [taxRate, setTaxRate] = useState<string | number>(() => lastTaxRatePercent())
  const [uploading, setUploading] = useState(false)
  const [error, setError] = useState<string | null>(null)

  const rate = Number(taxRate)
  const rateValid = String(taxRate).trim() !== '' && isValidTaxRate(rate)

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
      onUploaded(receipt)
    } catch (e) {
      setError(errorMessage(e))
    } finally {
      setUploading(false)
    }
  }

  return (
    <Card withBorder padding="md">
      <form
        onSubmit={(event) => {
          event.preventDefault()
          void upload()
        }}
      >
        <Stack gap="sm">
          <Title order={2}>New receipt</Title>
          <FileInput
            label="Receipt photo"
            placeholder="Choose a photo"
            accept="image/*"
            clearable
            value={file}
            onChange={setFile}
          />
          <NumberInput
            label="Sales tax"
            suffix="%"
            min={minTaxRatePercent}
            max={maxTaxRatePercent}
            step={0.5}
            decimalScale={3}
            allowNegative={false}
            error={rateValid ? null : `Enter a rate between ${minTaxRatePercent} and ${maxTaxRatePercent}%`}
            value={taxRate}
            onChange={setTaxRate}
          />
          <Button type="submit" disabled={!file || !rateValid} loading={uploading}>
            Upload
          </Button>
          {error && (
            <Alert color="red" variant="light">
              {error}
            </Alert>
          )}
        </Stack>
      </form>
    </Card>
  )
}
