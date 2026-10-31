import { cleanup, render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, expect, test, vi } from 'vitest'
import type { OperationResponse } from '../types/puzzle'
import OperationCards from './OperationCards'

// Remove rendered elements after each test so future tests start with a clean DOM.
afterEach(cleanup)

test('shows descriptions without buttons when onSelect is absent', () => {
  // Arrange: supply two cards, just as App does when displaying the puzzle preview.
  const operations: readonly OperationResponse[] = [
    { id: 1, description: 'Add 5', previewNumber: null },
    { id: 2, description: 'Multiply by 2', previewNumber: null },
  ]

  // Act: omitting onSelect selects preview mode. render mounts the component in jsdom.
  render(<OperationCards operations={operations} disabled={false} isBusy={false} />)

  // Assert: the player sees both descriptions in order, with no clickable card buttons.
  const descriptions = screen.getAllByRole('listitem').map(item => item.textContent)
  expect(descriptions).toEqual(['Add 5', 'Multiply by 2'])
  expect(screen.queryAllByRole('button')).toHaveLength(0)
})

test('hides computed results and reports the selected card ID', async () => {
  // Arrange: ID 7 differs from the card's array position, catching an accidental index lookup.
  const user = userEvent.setup()
  const onSelect = vi.fn()
  const card: OperationResponse = { id: 7, description: 'Add -3', previewNumber: 0 }
  render(
    <OperationCards operations={[card]} disabled={false} isBusy={false} onSelect={onSelect} />,
  )

  // The API's computed result stays hidden, while the card still reports its original ID.
  const button = screen.getByRole('button', { name: 'Add -3' })
  expect(button.textContent).toBe('Add -3')
  await user.click(button)

  // Assert: the spy records how often the callback ran and the ID it received.
  expect(onSelect).toHaveBeenCalledTimes(1)
  expect(onSelect).toHaveBeenCalledWith(7)

})

test('does not report a selection when the card is disabled', async () => {
  const user = userEvent.setup()
  const onSelect = vi.fn()
  const operations: readonly OperationResponse[] = [{ id: 7, description: 'Add 5', previewNumber: null }]
  render(
    <OperationCards operations={operations} disabled={true} isBusy={false} onSelect={onSelect} />,
  )

  const button = screen.getByRole('button', { name: 'Add 5' })
  expect(button.hasAttribute('disabled')).toBe(true)

  // userEvent respects disabled controls, just as a browser does.
  await user.click(button)

  expect(onSelect).not.toHaveBeenCalled()
})
