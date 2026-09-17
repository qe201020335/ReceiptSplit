import { Button, Card, Code, CopyButton, Group, Text, Title } from '@mantine/core'

export function SplitText({ text }: { text: string }) {
  return (
    <Card withBorder padding="md">
      <Group justify="space-between" mb="xs">
        <Title order={2}>Summary text</Title>
        <CopyButton value={text}>
          {({ copied, copy }) => (
            <Button
              size="xs"
              variant={copied ? 'light' : 'default'}
              color={copied ? 'green' : undefined}
              onClick={copy}
              disabled={text === ''}
            >
              {copied ? 'Copied' : 'Copy summary'}
            </Button>
          )}
        </CopyButton>
      </Group>
      {text === '' ? (
        <Text size="sm" c="dimmed">
          Each person's total and items show up here once names are added.
        </Text>
      ) : (
        <Code block mah="30rem" style={{ overflow: 'auto', whiteSpace: 'pre' }}>
          {text}
        </Code>
      )}
    </Card>
  )
}
