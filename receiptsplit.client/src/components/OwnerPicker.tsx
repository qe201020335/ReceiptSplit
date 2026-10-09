import { useState, type ReactElement } from 'react'
import { Combobox, Text, useCombobox } from '@mantine/core'
import type { UserSummary } from '../api.ts'
import { userLabel } from '../owners.ts'
import { OwnerAvatar } from './OwnerAvatar.tsx'
import classes from './OwnerPicker.module.css'

/** The option value for no owner; user ids are UUIDs, so it can't clash. */
const noOwner = 'none'

interface OwnerPickerProps {
  /** Null while loading. */
  users: UserSummary[] | null
  usersError: string | null
  /** The owner all the receipts already have, which can't be picked again: null for none, undefined for mixed. */
  currentOwnerId?: string | null
  /** The signed-in admin, marked You. */
  meId: string | null
  onPick: (ownerId: string | null) => void
  /** Called as it opens, so the people can be reloaded: someone may have signed up since. */
  onOpen?: () => void
  /** The button that opens the picker; it gets the function that toggles it. */
  children: (toggle: () => void) => ReactElement
}

/**
 * Searches people by name or email and picks one to own a receipt, or No owner, which stays offered below the
 * search results. Arrow keys, Enter and Escape work as in any Mantine combobox.
 */
export function OwnerPicker({ users, usersError, currentOwnerId, meId, onPick, onOpen, children }: OwnerPickerProps) {
  const [search, setSearch] = useState('')
  const combobox = useCombobox({
    onDropdownClose: () => {
      combobox.resetSelectedOption()
      combobox.focusTarget()
      setSearch('')
    },
    onDropdownOpen: () => {
      combobox.focusSearchInput()
      onOpen?.()
    },
  })

  const query = search.trim().toLowerCase()
  const matches = (users ?? []).filter(
    (user) =>
      query === '' || [user.name, user.email].some((text) => text?.toLowerCase().includes(query) === true),
  )
  const empty = usersError
    ? "Couldn't load people."
    : users === null
      ? 'Loading people…'
      : matches.length === 0
        ? `No one matches “${search.trim()}”`
        : null

  return (
    <Combobox
      store={combobox}
      width={300}
      position="bottom-end"
      shadow="md"
      // Combobox keeps closed dropdowns in the page by default: one per row would add a search box per receipt.
      keepMounted={false}
      onOptionSubmit={(value) => {
        onPick(value === noOwner ? null : value)
        combobox.closeDropdown()
      }}
    >
      <Combobox.Target withAriaAttributes={false}>{children(() => combobox.toggleDropdown())}</Combobox.Target>
      <Combobox.Dropdown>
        <Combobox.Search
          value={search}
          onChange={(event) => {
            setSearch(event.currentTarget.value)
            combobox.updateSelectedOptionIndex()
          }}
          placeholder="Search people"
          aria-label="Search people"
        />
        <Combobox.Options>
          {/* About six people show before it scrolls; No owner stays below, outside the scrolling part. */}
          <div className={classes.people}>
            {matches.map((user) => {
              const label = userLabel(user)
              const current = user.id === currentOwnerId
              const mark = current ? 'Current' : user.id === meId ? 'You' : null
              return (
                <Combobox.Option key={user.id} value={user.id} disabled={current} className={classes.option}>
                  <OwnerAvatar user={user} size={28} />
                  <div className={classes.person}>
                    <Text size="sm" truncate>
                      {label}
                    </Text>
                    {user.email && user.email !== label && (
                      <Text size="xs" c="dimmed" truncate>
                        {user.email}
                      </Text>
                    )}
                  </div>
                  {mark && (
                    <Text size="xs" c="dimmed" className={classes.mark}>
                      {mark}
                    </Text>
                  )}
                </Combobox.Option>
              )
            })}
            {empty && <Combobox.Empty>{empty}</Combobox.Empty>}
          </div>
          <div className={classes.divider} role="separator" />
          <Combobox.Option value={noOwner} disabled={currentOwnerId === null} className={classes.option}>
            <OwnerAvatar user={null} size={28} />
            <div className={classes.person}>
              <Text size="sm" className={classes.noOwner}>
                No owner
              </Text>
            </div>
            {currentOwnerId === null && (
              <Text size="xs" c="dimmed" className={classes.mark}>
                Current
              </Text>
            )}
          </Combobox.Option>
        </Combobox.Options>
      </Combobox.Dropdown>
    </Combobox>
  )
}
