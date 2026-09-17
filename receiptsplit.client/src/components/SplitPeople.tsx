import { Card, TagsInput, Text } from '@mantine/core'
import { modals } from '@mantine/modals'
import { hasAssignments, type SplitState } from '../splits.ts'
import type { SplitActions } from '../useSplitState.ts'

interface SplitPeopleProps {
  state: SplitState
  actions: SplitActions
}

export function SplitPeople({ state, actions }: SplitPeopleProps) {
  function change(names: string[]) {
    const losing = state.people.filter((person) => !names.includes(person) && hasAssignments(state, person))
    if (losing.length === 0) {
      actions.setPeople(names)
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
      onConfirm: () => actions.setPeople(names),
    })
  }

  return (
    <Card withBorder padding="md">
      <TagsInput
        label="People"
        placeholder="Name, Enter to add (e.g. Alice, Bob)"
        splitChars={[',']}
        clearable
        value={state.people}
        onChange={change}
      />
    </Card>
  )
}
