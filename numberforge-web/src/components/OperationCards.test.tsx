import { cleanup, render, screen } from '@testing-library/react'
import { afterEach, expect, test } from 'vitest'
import type { OperationResponse } from '../types/puzzle'
import OperationCards from './OperationCards'

// Remove rendered elements after each test so future tests start with a clean DOM.
afterEach(cleanup)

test('shows descriptions without buttons when onSelect is absent', () => {
  // Arrange: supply two cards, just as App does when displaying the puzzle preview.
  const operations: readonly OperationResponse[] = [
    { id: 1, description: 'Add 5' },
    { id: 2, description: 'Multiply by 2' },
  ]

  // Act: omitting onSelect selects preview mode. render mounts the component in jsdom.
  render(<OperationCards operations={operations} disabled={false} isBusy={false} />)

  // Assert: the player sees both descriptions in order, with no clickable card buttons.
  const descriptions = screen.getAllByRole('listitem').map(item => item.textContent)
  expect(descriptions).toEqual(['Add 5', 'Multiply by 2'])
  expect(screen.queryAllByRole('button')).toHaveLength(0)
})
