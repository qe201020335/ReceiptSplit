import { Avatar, Badge, Box, Menu, Text, UnstyledButton } from '@mantine/core'
import type { Account } from '../api.ts'
import { userLabel } from '../owners.ts'
import classes from './AccountMenu.module.css'

interface AccountMenuProps {
  account: Account
}

/** The signed-in user's initials in the header, opening who they are and Sign out. */
export function AccountMenu({ account }: AccountMenuProps) {
  // Named as the receipt manager names owners, so the initials and their color match.
  const name = userLabel(account)
  return (
    <Menu position="bottom-end" width={260}>
      <Menu.Target>
        <UnstyledButton className={classes.trigger} aria-label={`Account: ${name}`}>
          <Avatar name={name} color="initials" size={28} />
        </UnstyledButton>
      </Menu.Target>
      <Menu.Dropdown>
        <Box px="sm" py="xs">
          <Text size="sm" fw={500}>
            {name}
          </Text>
          {account.email && account.email !== name && (
            <Text size="xs" c="dimmed" className={classes.email}>
              {account.email}
            </Text>
          )}
          {account.isAdmin && (
            <Badge size="sm" variant="light" mt={6}>
              Admin
            </Badge>
          )}
        </Box>
        {account.signOutUrl && (
          <>
            <Menu.Divider />
            <Menu.Item component="a" href={account.signOutUrl}>
              Sign out
            </Menu.Item>
          </>
        )}
      </Menu.Dropdown>
    </Menu>
  )
}
