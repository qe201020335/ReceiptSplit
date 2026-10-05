import { Button, Card, Group, Stack, TagsInput, Text, Tooltip } from '@mantine/core'
import { modals } from '@mantine/modals'
import { hasAssignments, normalizePeople, type SplitState } from '../splits.ts'
import { useRecentNames } from '../useRecentNames.ts'
import type { SplitActions } from '../useSplitState.ts'

interface SplitPeopleProps {
  state: SplitState
  actions: SplitActions
}

export function SplitPeople({ state, actions }: SplitPeopleProps) {
  const [recentNames, recent] = useRecentNames()
  const chosen = new Set(state.people.map((person) => person.toLocaleLowerCase()))
  const offered = recentNames.filter((name) => !chosen.has(name.toLocaleLowerCase()))

  function apply(names: string[]) {
    const added = normalizePeople(names).filter((name) => !chosen.has(name.toLocaleLowerCase()))
    actions.setPeople(names)
    if (added.length > 0) {
      recent.remember(added)
    }
  }

  function change(names: string[]) {
    const losing = state.people.filter((person) => !names.includes(person) && hasAssignments(state, person))
    if (losing.length === 0) {
      apply(names)
      return
    }

    modals.openConfirmModal({
      title: `Remove ${losing.join(', ')}?`,
      centered: true,
      children: (
        <Text size="sm">
          {losing.length === 1 ? 'They have' : 'These people have'} items assigned, which go back to unassigned.
        </Text>
      ),
      labels: { confirm: 'Remove', cancel: 'Keep' },
      confirmProps: { color: 'red' },
      onConfirm: () => apply(names),
    })
  }

  return (
    <Card withBorder padding="md">
      <Stack gap="sm">
        <TagsInput
          label="People"
          placeholder="Name, Enter to add (e.g. Alice, Bob)"
          splitChars={[',']}
          clearable
          value={state.people}
          onChange={change}
        />
        {offered.length > 0 && (
          <Group gap="xs">
            <Text size="sm" c="dimmed">
              Add again:
            </Text>
            {offered.map((name) => (
              <Button.Group key={name}>
                <Button
                  variant="default"
                  size="compact-sm"
                  aria-label={`Add ${name}`}
                  onClick={() => apply([...state.people, name])}
                >
                  {name}
                </Button>
                <Tooltip label={`Forget ${name}`}>
                  <Button
                    variant="default"
                    size="compact-sm"
                    aria-label={`Forget ${name}`}
                    onClick={() => recent.forget(name)}
                  >
                    ×
                  </Button>
                </Tooltip>
              </Button.Group>
            ))}
          </Group>
        )}
      </Stack>
    </Card>
  )
}
