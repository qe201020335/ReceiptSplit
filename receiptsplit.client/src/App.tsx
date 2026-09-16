import { useCallback, useEffect, useState } from 'react'
import { Anchor, Box, Card, Container, Grid, Paper, Stack, Text } from '@mantine/core'
import { api, errorMessage, inProgress, type ReceiptSummary } from './api.ts'
import { ReceiptDetail } from './components/ReceiptDetail.tsx'
import { ReceiptList } from './components/ReceiptList.tsx'
import { UploadForm } from './components/UploadForm.tsx'
import { useSelectedReceipt } from './useSelectedReceipt.ts'

const pollIntervalMs = 2000

function App() {
  const [receipts, setReceipts] = useState<ReceiptSummary[] | null>(null)
  const [listError, setListError] = useState<string | null>(null)
  // Bumping this reloads the list.
  const [listVersion, setListVersion] = useState(0)
  const [selectedId, selectReceipt] = useSelectedReceipt()

  const reloadList = useCallback(() => setListVersion((version) => version + 1), [])

  useEffect(() => {
    let current = true
    api.listReceipts().then(
      (list) => {
        if (current) {
          setReceipts(list)
          setListError(null)
        }
      },
      (error: unknown) => {
        if (current) {
          setListError(errorMessage(error))
        }
      },
    )
    return () => {
      current = false
    }
  }, [listVersion])

  // Keep statuses in the list current while any receipt is still being read.
  const anyInProgress = receipts?.some((receipt) => inProgress(receipt.status)) ?? false
  useEffect(() => {
    if (!anyInProgress) {
      return
    }
    const timer = setTimeout(reloadList, pollIntervalMs)
    return () => clearTimeout(timer)
  }, [receipts, anyInProgress, reloadList])

  return (
    <Box mih="100vh" bg="var(--mantine-color-body)">
      <Paper component="header" withBorder radius={0} py="sm" px="md">
        <Anchor
          href="/"
          fw={600}
          size="lg"
          underline="never"
          c="var(--mantine-color-text)"
          onClick={(event) => {
            event.preventDefault()
            selectReceipt(null)
          }}
        >
          ReceiptSplit
        </Anchor>
      </Paper>
      <Container size="lg" py="md" px="md">
        <Grid gap="md" align="flex-start">
          <Grid.Col span={{ base: 12, sm: 5, md: 4 }}>
            <Stack gap="md">
              <UploadForm
                onUploaded={(receipt) => {
                  reloadList()
                  selectReceipt(receipt.id)
                }}
              />
              <ReceiptList receipts={receipts} error={listError} selectedId={selectedId} onSelect={selectReceipt} />
            </Stack>
          </Grid.Col>
          <Grid.Col span={{ base: 12, sm: 7, md: 8 }}>
            {selectedId ? (
              <ReceiptDetail
                key={selectedId}
                id={selectedId}
                onChanged={reloadList}
                onDeleted={() => {
                  selectReceipt(null)
                  reloadList()
                }}
              />
            ) : (
              <Card withBorder padding="xl">
                <Text c="dimmed" ta="center">
                  Upload a receipt photo, or pick a receipt from the list.
                </Text>
              </Card>
            )}
          </Grid.Col>
        </Grid>
      </Container>
    </Box>
  )
}

export default App
