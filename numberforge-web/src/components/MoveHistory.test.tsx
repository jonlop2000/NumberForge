import { cleanup, render, screen, within } from '@testing-library/react'
import { afterEach, expect, test } from 'vitest'
import type { MoveResponse } from '../types/puzzle'
import MoveHistory from './MoveHistory'

afterEach(cleanup)

test('hides the journey when history is empty', () => {
  render(<MoveHistory sessionId="session-1" history={[]} />)

  expect(screen.queryByRole('heading', { name: 'Your journey' })).toBeNull()
  expect(screen.queryByRole('list')).toBeNull()
})

test('displays completed moves in their original order', () => {
  const history: readonly MoveResponse[] = [
    { beforeNumber: 3, operationDescription: 'Add 5', afterNumber: 8 },
    { beforeNumber: 8, operationDescription: 'Multiply by 2', afterNumber: 16 },
    { beforeNumber: 16, operationDescription: 'Add -4', afterNumber: 12 },
  ]

  render(<MoveHistory sessionId="session-1" history={history} />)

  const journey = screen.getByRole('region', { name: 'Your journey' })
  const entries = within(journey).getAllByRole('listitem')
  expect(entries.map(entry => entry.textContent)).toEqual([
    '3 → Add 5 → 8',
    '8 → Multiply by 2 → 16',
    '16 → Add -4 → 12',
  ])
})
