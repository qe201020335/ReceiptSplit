import { useEffect, useState } from 'react'
import { api, type Account } from './api.ts'

/**
 * The signed-in user, loaded once. Null until then, and when it can't be loaded: a sign-in that can't be used is
 * reported through onSignInProblem, which shows its own page.
 */
export function useAccount(): Account | null {
  const [account, setAccount] = useState<Account | null>(null)

  useEffect(() => {
    let current = true
    api.me().then(
      (loaded) => {
        if (current) {
          setAccount(loaded)
        }
      },
      () => {},
    )
    return () => {
      current = false
    }
  }, [])

  return account
}
