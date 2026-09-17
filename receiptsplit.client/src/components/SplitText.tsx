import { useEffect, useState } from 'react'
import { Button, Card, Code, Group, Text, Title } from '@mantine/core'
import { notifications } from '@mantine/notifications'
import { errorMessage } from '../api.ts'
import { copyText } from '../clipboard.ts'

const copiedMs = 2000

export function SplitText({ text }: { text: string }) {
  const [copied, setCopied] = useState(false)

  useEffect(() => {
    if (!copied) {
      return
    }
    const timer = setTimeout(() => setCopied(false), copiedMs)
    return () => clearTimeout(timer)
  }, [copied])

  async function copy() {
    try {
      await copyText(text)
      setCopied(true)
    } catch (e) {
      notifications.show({ title: "Couldn't copy the summary", message: errorMessage(e), color: 'red' })
    }
  }

  return (
    <Card withBorder padding="md">
      <Group justify="space-between" mb="xs">
        <Title order={2}>Summary text</Title>
        <Button
          size="xs"
          variant={copied ? 'light' : 'default'}
          color={copied ? 'green' : undefined}
          onClick={() => void copy()}
          disabled={text === ''}
        >
          {copied ? 'Copied' : 'Copy summary'}
        </Button>
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
