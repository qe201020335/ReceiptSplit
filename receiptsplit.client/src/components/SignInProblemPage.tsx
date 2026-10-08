import { Button, Card, Stack, Text, Title } from '@mantine/core'
import type { SignInProblem, SignInProblemCode } from '../api.ts'
import { usePageTitle } from '../usePageTitle.ts'

interface SignInProblemPageProps {
  problem: SignInProblem
}

interface Explanation {
  title: string
  text: string
  /** Signing out lets the person sign in another way; reloading asks again, or sends them through sign-in. */
  action: 'sign-out' | 'reload'
  label: string
}

// Worded without naming a sign-in provider, so it stays right whichever ones are turned on.
const explanations: Record<SignInProblemCode, Explanation> = {
  'unsupported-sign-in': {
    title: "This sign-in method isn't supported",
    text: "You signed in with a method this app doesn't accept. Sign out, then sign in another way.",
    action: 'sign-out',
    label: 'Sign out',
  },
  'account-conflict': {
    title: 'Your email belongs to another account',
    text:
      "Another account here already uses your email, so you can't be signed in with it. Ask an admin to release " +
      'the email, then sign in again.',
    action: 'sign-out',
    label: 'Sign out',
  },
  'identity-unavailable': {
    title: "Your account couldn't be confirmed",
    text: 'Something went wrong while confirming who you are. Wait a moment, then try again.',
    action: 'reload',
    label: 'Try again',
  },
  'signed-out': {
    title: 'You were signed out',
    text: 'Your session has ended. Sign in again to carry on where you left off.',
    action: 'reload',
    label: 'Sign in again',
  },
}

/** Shown in place of the app when the sign-in can't be used, with the one thing that can be done about it. */
export function SignInProblemPage({ problem }: SignInProblemPageProps) {
  const explanation = explanations[problem.code]
  usePageTitle(explanation.title)
  // Without a sign-out address there's nothing to sign out of, and asking again is all that's left.
  const signOut = explanation.action === 'sign-out' ? problem.signOutUrl : null

  return (
    <Card withBorder padding="xl" maw={520} mx="auto">
      <Stack gap="md" align="flex-start">
        <Title order={2}>{explanation.title}</Title>
        <Text>{explanation.text}</Text>
        {signOut ? (
          <Button component="a" href={signOut}>
            {explanation.label}
          </Button>
        ) : (
          <Button onClick={() => window.location.reload()}>
            {explanation.action === 'sign-out' ? 'Try again' : explanation.label}
          </Button>
        )}
      </Stack>
    </Card>
  )
}
