import { Avatar } from '@mantine/core'
import type { UserSummary } from '../api.ts'
import { userLabel } from '../owners.ts'
import classes from './OwnerAvatar.module.css'

interface OwnerAvatarProps {
  /** Null for no owner. */
  user: UserSummary | null
  /** An owner who can't be named yet, or isn't in the list: a plain placeholder, never the no-owner outline. */
  unknown?: boolean
  size?: number
}

/**
 * A person's initials, colored from their label as the header's account menu colors them, so someone looks the same
 * everywhere. No owner is an empty dashed outline: the state admins look for, told apart by shape, not color.
 */
export function OwnerAvatar({ user, unknown = false, size = 22 }: OwnerAvatarProps) {
  if (unknown) {
    return <Avatar size={size} color="gray" aria-hidden />
  }
  return user ? (
    <Avatar name={userLabel(user)} color="initials" size={size} />
  ) : (
    <Avatar size={size} variant="transparent" color="gray" className={classes.none} aria-hidden />
  )
}
