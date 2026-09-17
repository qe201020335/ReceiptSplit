import { useCallback, useEffect, useRef, useState } from 'react'
import { Anchor, Box, Card, Container, Grid, Paper, Stack, Text } from '@mantine/core'
import { api, errorMessage, inProgress, type ReceiptSummary } from './api.ts'
import { ReceiptDetail } from './components/ReceiptDetail.tsx'
import { ReceiptList } from './components/ReceiptList.tsx'
import { UploadForm } from './components/UploadForm.tsx'
import { SplitsPage } from './components/SplitsPage.tsx'
import { useRoute } from './useRoute.ts'

const pollIntervalMs = 2000

function App() {
  const [receipts, setReceipts] = useState<ReceiptSummary[] | null>(null)
  const [listError, setListError] = useState<string | null>(null)
  // Bumping this reloads the list.
  const [listVersion, setListVersion] = useState(0)
  const [route, navigate, goBack] = useRoute()
  const selectedId = route.receiptId

  const reloadList = useCallback(() => setListVersion((version) => version + 1), [])

  // On a phone the detail sits below the upload form and the list, so opening a receipt scrolls down to it.
  const detailRef = useRef<HTMLDivElement>(null)
  const revealDetail = useRef(false)
  const openReceipt = useCallback(
    (id: string) => {
      revealDetail.current = true
      navigate(id)
    },
    [navigate],
  )

  useEffect(() => {
    const detail = detailRef.current
    if (!revealDetail.current || !detail) {
      return
    }
    revealDetail.current = false
    // Side by side (from Mantine's sm breakpoint up) the detail is already in view.
    if (window.matchMedia('(max-width: 47.99em)').matches) {
      const smooth = !window.matchMedia('(prefers-reduced-motion: reduce)').matches
      detail.scrollIntoView({ behavior: smooth ? 'smooth' : 'auto', block: 'start' })
    }
  }, [selectedId])

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
    <Box mih="100vh" bg="var(--app-bg)">
      <Paper component="header" radius={0} py="sm" px="md" bd="0 0 1px 0 solid var(--mantine-color-default-border)">
        <Anchor
          href="/"
          fw={600}
          size="lg"
          underline="never"
          c="var(--mantine-color-text)"
          onClick={(event) => {
            event.preventDefault()
            navigate(null)
          }}
        >
          ReceiptSplit
        </Anchor>
      </Paper>
      {selectedId && route.view === 'splits' ? (
        <Container size="xl" py="md" px="md">
          <SplitsPage key={selectedId} id={selectedId} onBack={() => goBack(selectedId)} />
        </Container>
      ) : (
        <Container size="lg" py="md" px="md">
          <Grid gap="md" align="flex-start">
            <Grid.Col span={{ base: 12, sm: 5, md: 4 }}>
              <Stack gap="md">
                <UploadForm
                  onUploaded={(receipt) => {
                    reloadList()
                    openReceipt(receipt.id)
                  }}
                />
                <ReceiptList receipts={receipts} error={listError} selectedId={selectedId} onSelect={openReceipt} />
              </Stack>
            </Grid.Col>
            <Grid.Col
              span={{ base: 12, sm: 7, md: 8 }}
              ref={detailRef}
              // At least a screen tall while stacked, so there is room to scroll it to the top before it has loaded.
              mih={selectedId ? { base: '100dvh', sm: 0 } : undefined}
              style={{ scrollMarginTop: 16 }}
            >
              {selectedId ? (
                <ReceiptDetail
                  key={selectedId}
                  id={selectedId}
                  onChanged={reloadList}
                  onSplit={() => navigate(selectedId, 'splits')}
                  onDeleted={() => {
                    navigate(null)
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
      )}
    </Box>
  )
}

export default App
