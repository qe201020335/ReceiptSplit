import { useEffect, useRef, useState } from 'react'
import { ActionIcon, Button, Group, Modal, Slider, Text, Tooltip } from '@mantine/core'
import { useElementSize, useMediaQuery } from '@mantine/hooks'
import { notifications } from '@mantine/notifications'
import ReactCrop from 'react-image-crop'
import 'react-image-crop/dist/ReactCrop.css'
import { errorMessage } from '../api.ts'
import {
  drawPreview,
  fullCrop,
  isEdited,
  maxStraighten,
  noEdits,
  renderEdited,
  rotatedSize,
  turnCrop,
  type PhotoEdits,
} from '../photoEdits.ts'
import classes from './PhotoEditor.module.css'

/** The crop tool works over a copy no larger than this; the upload is rendered from the full photo. */
const previewMaxSide = 2048

/** Room around the photo on the stage, so the crop handles at its edges aren't cut off. */
const stagePadding = 20

interface PhotoEditorProps {
  opened: boolean
  image: HTMLImageElement
  source: File
  edits: PhotoEdits
  onClose: () => void
  /** The edited photo, or null when the edits were all undone and the original should be uploaded. */
  onApply: (edits: PhotoEdits, edited: File | null) => void
}

export function PhotoEditor({ opened, onClose, ...props }: PhotoEditorProps) {
  const compact = useMediaQuery('(max-width: 36em)', undefined, { getInitialValueInEffect: false })
  return (
    <Modal
      opened={opened}
      onClose={onClose}
      title="Crop and rotate"
      size="xl"
      fullScreen={compact}
      classNames={{ content: classes.content, body: classes.body }}
    >
      <EditorBody {...props} onClose={onClose} />
    </Modal>
  )
}

function EditorBody({ image, source, edits: initial, onClose, onApply }: Omit<PhotoEditorProps, 'opened'>) {
  const [edits, setEdits] = useState(initial)
  const [straightening, setStraightening] = useState(false)
  const [rendering, setRendering] = useState(false)
  const canvasRef = useRef<HTMLCanvasElement>(null)
  const { ref: stageRef, width: stageWidth, height: stageHeight } = useElementSize()

  const previewScale = Math.min(1, previewMaxSide / Math.max(image.naturalWidth, image.naturalHeight))
  const box = rotatedSize(image.naturalWidth, image.naturalHeight, edits)
  const fit = Math.min((stageWidth - 2 * stagePadding) / box.width, (stageHeight - 2 * stagePadding) / box.height)
  const measured = fit > 0

  const { quarterTurns, straighten } = edits
  useEffect(() => {
    if (canvasRef.current) {
      drawPreview(canvasRef.current, image, { ...noEdits, quarterTurns, straighten }, previewScale)
    }
  }, [image, quarterTurns, straighten, previewScale, measured])

  function turn(clockwise: boolean) {
    setEdits((current) => ({
      ...current,
      quarterTurns: (current.quarterTurns + (clockwise ? 1 : 3)) % 4,
      crop: turnCrop(current.crop, clockwise),
    }))
  }

  async function apply() {
    if (!isEdited(edits)) {
      onApply(noEdits, null)
      return
    }
    setRendering(true)
    try {
      onApply(edits, await renderEdited(image, edits, source))
    } catch (e) {
      notifications.show({ title: "Couldn't edit the photo", message: errorMessage(e), color: 'red' })
      setRendering(false)
    }
  }

  return (
    <>
      <div ref={stageRef} className={classes.stage}>
        {measured && (
          <ReactCrop
            crop={edits.crop}
            onChange={(_, crop) => {
              // A tap without a drag reports an empty crop; keep the one there is.
              if (crop.width > 0 && crop.height > 0) {
                setEdits((current) => ({ ...current, crop }))
              }
            }}
            keepSelection
            minWidth={24}
            minHeight={24}
            className={classes.crop}
            ariaLabels={{
              cropArea: 'Part of the photo to keep; use the arrow keys to move it',
              nwDragHandle: 'Top left corner',
              nDragHandle: 'Top edge',
              neDragHandle: 'Top right corner',
              eDragHandle: 'Right edge',
              seDragHandle: 'Bottom right corner',
              sDragHandle: 'Bottom edge',
              swDragHandle: 'Bottom left corner',
              wDragHandle: 'Left edge',
            }}
          >
            <div className={classes.media} data-straightening={straightening || undefined}>
              <canvas
                ref={canvasRef}
                className={classes.canvas}
                style={{ width: box.width * fit, height: box.height * fit }}
                role="img"
                aria-label="The receipt photo"
              />
            </div>
          </ReactCrop>
        )}
      </div>
      <div className={classes.tools}>
        <Group gap="xs" wrap="nowrap" className={classes.adjust}>
          <Tooltip label="Rotate left">
            <ActionIcon variant="default" size="lg" aria-label="Rotate left" onClick={() => turn(false)}>
              ↺
            </ActionIcon>
          </Tooltip>
          <Tooltip label="Rotate right">
            <ActionIcon variant="default" size="lg" aria-label="Rotate right" onClick={() => turn(true)}>
              ↻
            </ActionIcon>
          </Tooltip>
          <Text size="sm" className={classes.straightenLabel} aria-hidden>
            Straighten
          </Text>
          <Slider
            className={classes.slider}
            min={-maxStraighten}
            max={maxStraighten}
            step={0.5}
            marks={[{ value: 0 }]}
            label={null}
            value={straighten}
            onChange={(value) => {
              setStraightening(true)
              setEdits((current) => ({ ...current, straighten: value }))
            }}
            onChangeEnd={() => setStraightening(false)}
            thumbLabel="Straighten"
            thumbValueText={(value) => `${value} degrees`}
          />
          <Text size="sm" className={classes.degrees} aria-hidden>
            {straighten > 0 ? '+' : ''}
            {straighten}°
          </Text>
        </Group>
        <Group justify="space-between" gap="xs">
          <Button
            variant="subtle"
            color="gray"
            disabled={!isEdited(edits)}
            onClick={() => setEdits({ ...noEdits, crop: fullCrop })}
          >
            Reset
          </Button>
          <Group gap="xs">
            <Button variant="default" onClick={onClose}>
              Cancel
            </Button>
            <Button loading={rendering} onClick={() => void apply()}>
              Apply
            </Button>
          </Group>
        </Group>
      </div>
    </>
  )
}
