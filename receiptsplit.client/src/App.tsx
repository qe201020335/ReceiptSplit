import { useCallback, useEffect, useState } from 'react'
import { Anchor, Box, Card, Container, Grid, Group, Paper, Stack, Text, Title } from '@mantine/core'
import { useMediaQuery } from '@mantine/hooks'
import { api, errorMessage, inProgress, onSignInProblem, type ReceiptSummary, type SignInProblem } from './api.ts'
import { AccountMenu } from './components/AccountMenu.tsx'
import { ReceiptDetail } from './components/ReceiptDetail.tsx'
import { ReceiptList } from './components/ReceiptList.tsx'
import { ReceiptManager } from './components/ReceiptManager.tsx'
import { ReceiptPage } from './components/ReceiptPage.tsx'
import { SignInProblemPage } from './components/SignInProblemPage.tsx'
import { UploadForm } from './components/UploadForm.tsx'
import { SplitsPage } from './components/SplitsPage.tsx'
import { useAccount } from './useAccount.ts'
import { home, useRoute, type Route } from './useRoute.ts'

const pollIntervalMs = 2000

function App() {
  // Set by any request that shows the sign-in can't be used; the app then explains instead of showing receipts.
  // Subscribed before the requests below start.
  const [signInProblem, setSignInProblem] = useState<SignInProblem | null>(null)
  useEffect(() => onSignInProblem(setSignInProblem), [])
  const account = useAccount()
  const [receipts, setReceipts] = useState<ReceiptSummary[] | null>(null)
  const [listError, setListError] = useState<string | null>(null)
  // Bumping this reloads the list.
  const [listVersion, setListVersion] = useState(0)
  const [location, go, goBack, replace] = useRoute()
  // Below Mantine's sm breakpoint the start page stacks, which would put an open receipt below the upload form and
  // the list. Phones open receipts on their own page instead, and a start page link to a receipt redirects there.
  const stacked = useMediaQuery('(max-width: 47.99em)', undefined, { getInitialValueInEffect: false })
  const redirect = stacked && location.page === 'home' && location.receiptId !== null ? location.receiptId : null
  // Drawn as the receipt page while the redirect below happens, so the stacked start page never shows.
  const route: Route = redirect ? { page: 'receipt', receiptId: redirect } : location
  const selectedId = route.page === 'home' ? route.receiptId : null

  useEffect(() => {
    if (redirect) {
      replace({ page: 'receipt', receiptId: redirect })
    }
  }, [redirect, replace])

  // Receipts picked on the list to split together, or null when not picking. Kept while on the splits page, so
  // coming back lets people change the set.
  const [picked, setPicked] = useState<string[] | null>(null)
  const reloadList = useCallback(() => setListVersion((version) => version + 1), [])

  const openReceipt = (id: string) => go(stacked ? { page: 'receipt', receiptId: id } : { page: 'home', receiptId: id })

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

  // The splits page has a wide table beside its summary, so it gets a wider page; the manager and a receipt's own
  // page are a single column.
  const containerSize = route.page === 'splits' ? 'xl' : route.page === 'home' ? 'lg' : 'md'

  return (
    <Box mih="100vh" bg="var(--app-bg)">
      <Paper component="header" radius={0} py="sm" bd="0 0 1px 0 solid var(--mantine-color-default-border)">
        {/* The same container as the page below, so the name lines up with the cards' left edge. */}
        <Container size={containerSize} px="md">
          <Group justify="space-between" wrap="nowrap" gap="md">
            <Title order={1} fz="lg" fw={600} lh={1.55}>
              <Anchor
                href="/"
                inherit
                underline="never"
                c="var(--mantine-color-text)"
                onClick={(event) => {
                  event.preventDefault()
                  go(home)
                }}
              >
                ReceiptSplit
              </Anchor>
            </Title>
            {account && !signInProblem && <AccountMenu account={account} />}
          </Group>
        </Container>
      </Paper>
      {signInProblem ? (
        <Container size={containerSize} py="xl" px="md">
          <SignInProblemPage problem={signInProblem} />
        </Container>
      ) : route.page === 'splits' ? (
        <Container size={containerSize} py="md" px="md">
          <SplitsPage
            key={route.ids.join(',')}
            ids={route.ids}
            // Opened by a link, a single receipt's split goes back to that receipt, several to the list.
            onBack={() => goBack(route.ids.length === 1 ? { page: 'receipt', receiptId: route.ids[0] } : home)}
          />
        </Container>
      ) : route.page === 'manage' ? (
        <Container size={containerSize} py="md" px="md">
          <ReceiptManager
            receipts={receipts}
            error={listError}
            onChanged={reloadList}
            onOpen={(id) => go({ page: 'receipt', receiptId: id })}
            onBack={() => goBack(home)}
          />
        </Container>
      ) : route.page === 'receipt' ? (
        <Container size={containerSize} py="md" px="md">
          <ReceiptPage
            id={route.receiptId}
            onChanged={reloadList}
            onSplit={() => go({ page: 'splits', ids: [route.receiptId] })}
            onDeleted={() => {
              reloadList()
              goBack({ page: 'manage' })
            }}
            onBack={() => goBack({ page: 'manage' })}
          />
        </Container>
      ) : (
        <Container size={containerSize} py="md" px="md">
          <Grid gap="md" align="flex-start">
            <Grid.Col span={{ base: 12, sm: 5, md: 4 }}>
              <Stack gap="md">
                <UploadForm
                  onUploaded={(receipt) => {
                    reloadList()
                    openReceipt(receipt.id)
                  }}
                />
                <ReceiptList
                  receipts={receipts}
                  error={listError}
                  selectedId={selectedId}
                  onSelect={openReceipt}
                  picked={picked}
                  onPickedChange={setPicked}
                  onSplit={(ids) => go({ page: 'splits', ids })}
                  onManage={() => go({ page: 'manage' })}
                />
              </Stack>
            </Grid.Col>
            <Grid.Col span={{ base: 12, sm: 7, md: 8 }}>
              {selectedId ? (
                <ReceiptDetail
                  key={selectedId}
                  id={selectedId}
                  onChanged={reloadList}
                  onSplit={() => go({ page: 'splits', ids: [selectedId] })}
                  onDeleted={() => {
                    go(home)
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
