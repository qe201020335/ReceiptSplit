import { Button, Card, Group, Stack, Title } from '@mantine/core'
import { ReceiptDetail } from './ReceiptDetail.tsx'

interface ReceiptPageProps {
  id: string
  onChanged: () => void
  onDeleted: () => void
  onSplit: () => void
  onBack: () => void
}

/**
 * One receipt on its own page, as opened from the receipt manager or on a phone. The receipt card names the store,
 * and also the browser tab, so the header only says what kind of page this is.
 */
export function ReceiptPage({ id, onChanged, onDeleted, onSplit, onBack }: ReceiptPageProps) {
  return (
    <Stack gap="md">
      <Card withBorder padding="md">
        <Group gap="md" wrap="nowrap">
          <Button variant="default" onClick={onBack} style={{ flexShrink: 0 }}>
            ← Back
          </Button>
          <Title order={2}>Receipt details</Title>
        </Group>
      </Card>
      <ReceiptDetail key={id} id={id} onChanged={onChanged} onDeleted={onDeleted} onSplit={onSplit} />
    </Stack>
  )
}
