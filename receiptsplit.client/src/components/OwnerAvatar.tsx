import { Avatar } from '@mantine/core'
import type { UserSummary } from '../api.ts'
import { userLabel } from '../owners.ts'
import classes from './OwnerAvatar.module.css'

interface OwnerAvatarProps {
  /** Null for no owner. */
  user: UserSummary | null
  size?: number
}

/**
 * A person's initials, colored from their label as the header's account menu colors them, so someone looks the same
 * everywhere. No owner is an empty dashed outline: the state admins look for, told apart by shape, not color.
 */
export function OwnerAvatar({ user, size = 22 }: OwnerAvatarProps) {
  return user ? (
    <Avatar name={userLabel(user)} color="initials" size={size} />
  ) : (
    <Avatar size={size} variant="transparent" color="gray" className={classes.none} aria-hidden />
  )
}
