import { useState } from 'react'
import { Button, Card, CloseButton, Group, NumberInput, Stack, Text, Title } from '@mantine/core'
import { Dropzone } from '@mantine/dropzone'
import { notifications } from '@mantine/notifications'
import { api, errorMessage, type ReceiptQueued } from '../api.ts'
import { isValidTaxRate, lastTaxRatePercent, maxTaxRatePercent, minTaxRatePercent, rememberTaxRatePercent } from '../taxRate.ts'
import { acceptedPhotoFormats, acceptedPhotoTypes, maxUploadBytes } from '../uploadTypes.ts'

interface UploadFormProps {
  onUploaded: (receipt: ReceiptQueued) => void
}

export function UploadForm({ onUploaded }: UploadFormProps) {
  const [file, setFile] = useState<File | null>(null)
  const [taxRate, setTaxRate] = useState<string | number>(() => lastTaxRatePercent())
  const [uploading, setUploading] = useState(false)

  const rate = Number(taxRate)
  const rateValid = String(taxRate).trim() !== '' && isValidTaxRate(rate)

  async function upload() {
    if (!file || !rateValid) {
      return
    }

    setUploading(true)
    try {
      const receipt = await api.uploadReceipt(file, rate)
      rememberTaxRatePercent(rate)
      setFile(null)
      onUploaded(receipt)
      notifications.show({ message: 'Photo uploaded, reading the receipt…', color: 'blue' })
    } catch (e) {
      notifications.show({ title: "Couldn't upload the photo", message: errorMessage(e), color: 'red' })
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
          <div>
            <Text size="sm" fw={500} mb={4}>
              Receipt photo
            </Text>
            <Dropzone
              onDrop={(files) => setFile(files[0] ?? null)}
              onReject={(rejections) =>
                notifications.show({
                  title: "That file can't be used",
                  message:
                    rejections[0]?.errors[0]?.code === 'file-too-large'
                      ? 'The photo is larger than 30 MB.'
                      : `Choose a ${acceptedPhotoFormats} photo.`,
                  color: 'red',
                })
              }
              accept={acceptedPhotoTypes}
              maxSize={maxUploadBytes}
              maxFiles={1}
              multiple={false}
              py="lg"
              px="md"
            >
              <Stack gap={4} align="center" style={{ pointerEvents: 'none' }}>
                <Dropzone.Accept>
                  <Text size="sm">Drop the photo</Text>
                </Dropzone.Accept>
                <Dropzone.Reject>
                  <Text size="sm" c="red">
                    Photos only, up to 30 MB
                  </Text>
                </Dropzone.Reject>
                <Dropzone.Idle>
                  <Text size="sm" ta="center">
                    Drag a photo here or click to choose
                  </Text>
                </Dropzone.Idle>
              </Stack>
            </Dropzone>
            {file && (
              <Group gap="xs" mt="xs" wrap="nowrap">
                <Text size="sm" truncate>
                  {file.name}
                </Text>
                <CloseButton size="sm" aria-label="Clear the chosen photo" onClick={() => setFile(null)} />
              </Group>
            )}
          </div>
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
        </Stack>
      </form>
    </Card>
  )
}
