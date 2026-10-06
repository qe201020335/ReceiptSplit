import { useEffect, useRef, useState } from 'react'
import {
  Button,
  Card,
  Checkbox,
  CloseButton,
  Group,
  Loader,
  NumberInput,
  Stack,
  Text,
  Title,
  UnstyledButton,
} from '@mantine/core'
import { Dropzone } from '@mantine/dropzone'
import { notifications } from '@mantine/notifications'
import { api, errorMessage, type ReceiptQueued } from '../api.ts'
import { decodePhoto, noEdits, type PhotoEdits } from '../photoEdits.ts'
import { isValidTaxRate, lastTaxRatePercent, maxTaxRatePercent, minTaxRatePercent, rememberTaxRatePercent } from '../taxRate.ts'
import { acceptedPhotoFormats, acceptedPhotoTypes, maxUploadBytes } from '../uploadTypes.ts'
import { PhotoEditor } from './PhotoEditor.tsx'
import classes from './UploadForm.module.css'

interface ChosenPhoto {
  original: File
  url: string
  /** Undefined while decoding, null when the browser can't show the photo (HEIC outside Safari) to edit it. */
  image: HTMLImageElement | null | undefined
  edits: PhotoEdits
  /** The cropped and rotated photo, or null to upload the original as it is. */
  edited: File | null
  editedUrl: string | null
}

interface UploadFormProps {
  onUploaded: (receipt: ReceiptQueued) => void
}

export function UploadForm({ onUploaded }: UploadFormProps) {
  const [photo, setPhoto] = useState<ChosenPhoto | null>(null)
  // The same photo, for the decode that finishes later and the unmount cleanup to see the current one.
  const photoRef = useRef<ChosenPhoto | null>(null)
  const [editing, setEditing] = useState(false)
  const [taxRate, setTaxRate] = useState<string | number>(() => lastTaxRatePercent())
  const [taxIncluded, setTaxIncluded] = useState(false)
  const [uploading, setUploading] = useState(false)

  const rate = Number(taxRate)
  const rateValid = String(taxRate).trim() !== '' && isValidTaxRate(rate)

  /** Shows another photo, or none, and frees the object URLs of the one it replaces. */
  function showPhoto(next: ChosenPhoto | null) {
    const current = photoRef.current
    for (const url of [current?.url, current?.editedUrl]) {
      if (url && url !== next?.url && url !== next?.editedUrl) {
        URL.revokeObjectURL(url)
      }
    }
    photoRef.current = next
    setPhoto(next)
  }

  useEffect(
    () => () => {
      const current = photoRef.current
      for (const url of [current?.url, current?.editedUrl]) {
        if (url) {
          URL.revokeObjectURL(url)
        }
      }
    },
    [],
  )

  async function choose(file: File) {
    const chosen: ChosenPhoto = {
      original: file,
      url: URL.createObjectURL(file),
      image: undefined,
      edits: noEdits,
      edited: null,
      editedUrl: null,
    }
    showPhoto(chosen)
    const image = await decodePhoto(chosen.url)
    // Another photo may have been chosen while this one decoded.
    if (photoRef.current?.url === chosen.url) {
      showPhoto({ ...photoRef.current, image })
    }
  }

  function applyEdits(edits: PhotoEdits, edited: File | null) {
    const current = photoRef.current
    if (current) {
      showPhoto({ ...current, edits, edited, editedUrl: edited ? URL.createObjectURL(edited) : null })
    }
    setEditing(false)
  }

  async function upload() {
    const file = photo?.edited ?? photo?.original
    if (!file || !rateValid) {
      return
    }

    setUploading(true)
    try {
      const receipt = await api.uploadReceipt(file, rate, taxIncluded)
      rememberTaxRatePercent(rate)
      showPhoto(null)
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
            {photo ? (
              <>
                <PhotoPreview photo={photo} onEdit={() => setEditing(true)} />
                <Group gap="xs" mt="xs" wrap="nowrap">
                  <Text size="sm" truncate flex={1}>
                    {photo.original.name}
                  </Text>
                  {photo.image && (
                    <Button variant="default" size="xs" onClick={() => setEditing(true)}>
                      Crop and rotate
                    </Button>
                  )}
                  <CloseButton size="sm" aria-label="Clear the chosen photo" onClick={() => showPhoto(null)} />
                </Group>
                {photo.image && (
                  <PhotoEditor
                    opened={editing}
                    image={photo.image}
                    source={photo.original}
                    edits={photo.edits}
                    onClose={() => setEditing(false)}
                    onApply={applyEdits}
                  />
                )}
              </>
            ) : (
              <Dropzone
                onDrop={(files) => {
                  if (files[0]) {
                    void choose(files[0])
                  }
                }}
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
            disabled={taxIncluded}
            error={rateValid ? null : `Enter a rate between ${minTaxRatePercent} and ${maxTaxRatePercent}%`}
            value={taxRate}
            onChange={setTaxRate}
          />
          <Checkbox
            label="Prices include tax"
            description="As in Japan: the tax is part of each price and isn't checked."
            checked={taxIncluded}
            onChange={(event) => setTaxIncluded(event.currentTarget.checked)}
          />
          <Button type="submit" disabled={!photo || photo.image === undefined || !rateValid} loading={uploading}>
            Upload
          </Button>
        </Stack>
      </form>
    </Card>
  )
}

/** The chosen photo as it will be uploaded, which opens the editor when the browser can show it. */
function PhotoPreview({ photo, onEdit }: { photo: ChosenPhoto; onEdit: () => void }) {
  if (photo.image === undefined) {
    return (
      <div className={classes.preview}>
        <Loader size="sm" color="gray.3" aria-label="Opening the photo" />
      </div>
    )
  }
  if (photo.image === null) {
    return (
      <div className={classes.preview}>
        <Text size="sm" ta="center" className={classes.unsupported}>
          This browser can't show this kind of photo, so it can't be cropped here. It will be uploaded as it is.
        </Text>
      </div>
    )
  }
  return (
    <UnstyledButton className={classes.preview} onClick={onEdit} aria-label="Crop and rotate the photo">
      <img src={photo.editedUrl ?? photo.url} alt="" className={classes.thumbnail} />
    </UnstyledButton>
  )
}
