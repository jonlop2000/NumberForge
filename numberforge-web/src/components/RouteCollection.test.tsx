import { StrictMode } from 'react'
import { cleanup, render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, expect, test, vi } from 'vitest'
import type { PuzzleProgressResponse, SessionResponse } from '../types/puzzle'
import { collectionStoragePrefix, discoveryFromWin, loadDiscoveries, saveDiscovery } from '../lib/routeCollection'
import RouteCollection from './RouteCollection'

const key = 'c'.repeat(64)
const routeA = 'a'.repeat(64)
const routeB = 'b'.repeat(64)
function winningSession(overrides: Partial<SessionResponse> = {}): SessionResponse {
  return {
    sessionId: 'first-win', puzzleId: 'demo', puzzleName: 'First Forge',
    collectionKey: key, totalRoutes: 2, routeId: routeA, revision: 2,
    playUntilUtc: null, expiresAtUtc: '2099-01-02T00:00:00Z', status: 'won',
    currentNumber: 14, targetNumber: 14, movesUsed: 2, maxMoves: 2,
    elapsedSeconds: 10, isPerfect: true, availableOperations: [],
    charm: { name: 'Echo', description: 'Your first positive addition activates twice.', isUsed: true },
    history: [
      { beforeNumber: 3, operationDescription: 'Add 2 (Echo ×2)', afterNumber: 7 },
      { beforeNumber: 7, operationDescription: 'Multiply by 2', afterNumber: 14 },
    ], ...overrides,
  }
}
function progress(routeIds = [routeA], collectionKey = key): PuzzleProgressResponse {
  return { puzzleId: 'demo', collectionKey, hasSolved: routeIds.length > 0,
    totalRoutes: routeIds.length > 0 ? 2 : null, foundRoutes: routeIds.length,
    bestMoves: routeIds.length > 0 ? 2 : null, routeIds }
}

afterEach(() => { cleanup(); vi.restoreAllMocks(); localStorage.clear() })

test('records journey details once, restores them, and recognizes a repeated verified route', async () => {
  const user = userEvent.setup()
  const session = winningSession()
  const view = render(<StrictMode><RouteCollection collectionKey={key} progress={progress()} session={session} /></StrictMode>)
  await screen.findByText('Routes discovered: 1/2')
  await screen.findByText('New route discovered!')
  expect(loadDiscoveries(key).routes).toHaveLength(1)
  await user.click(screen.getByText('Your discovered routes'))
  await user.click(screen.getByText('Route 1 · 2 cards · Perfect'))
  expect(screen.getByText('3 → Add 2 (Echo ×2) → 7')).toBeDefined()
  view.rerender(<RouteCollection collectionKey={key} progress={progress()} session={winningSession({ sessionId: 'replay' })} />)
  await screen.findByText('Route already discovered.')
  expect(screen.getByText('Routes discovered: 1/2')).toBeDefined()
})

test('browser entries cannot unlock a collection or inflate the verified count', async () => {
  saveDiscovery(key, discoveryFromWin(winningSession())!)
  saveDiscovery(key, discoveryFromWin(winningSession({ routeId: routeB }))!)
  const view = render(<RouteCollection collectionKey={key} progress={progress([])} session={winningSession()} />)
  expect(screen.queryByRole('region', { name: 'Route collection' })).toBeNull()
  view.rerender(<RouteCollection collectionKey={key} progress={progress()} session={null} />)
  await screen.findByText('Routes discovered: 1/2')
  expect(screen.queryByText('All routes discovered!')).toBeNull()
  expect(screen.queryByText(/Route 2 ·/)).toBeNull()
})

test('verified progress survives an empty or cleared browser cache', async () => {
  const view = render(<RouteCollection collectionKey={key} progress={progress([routeA, routeB])} session={null} />)
  await screen.findByText('Routes discovered: 2/2')
  expect(screen.getByText('All routes discovered!')).toBeDefined()
  expect(screen.getByText('Best route: 2 cards')).toBeDefined()
  expect(screen.queryByText('Your discovered routes')).toBeNull()
  localStorage.clear()
  window.dispatchEvent(new StorageEvent('storage', { key: null, newValue: null }))
  await waitFor(() => expect(screen.getByText('Routes discovered: 2/2')).toBeDefined())
  view.rerender(<RouteCollection collectionKey={'d'.repeat(64)} progress={progress([], 'd'.repeat(64))} session={null} />)
  expect(screen.queryByRole('region', { name: 'Route collection' })).toBeNull()
})

test('blocked cache writes preserve the server count and current journey', async () => {
  vi.spyOn(Storage.prototype, 'setItem').mockImplementation(() => { throw new DOMException('Storage full', 'QuotaExceededError') })
  render(<RouteCollection collectionKey={key} progress={progress()} session={winningSession()} />)
  await screen.findByText('Routes discovered: 1/2')
  await screen.findByText(/Your route count is saved/)
  expect(screen.getByText('3 → Add 2 (Echo ×2) → 7')).toBeDefined()
})

test('storage events can add verified journey details but never change the server count', async () => {
  render(<RouteCollection collectionKey={key} progress={progress()} session={null} />)
  const discovery = discoveryFromWin(winningSession())!
  saveDiscovery(key, discovery)
  window.dispatchEvent(new StorageEvent('storage', { key: collectionStoragePrefix(key) + discovery.routeId, newValue: JSON.stringify(discovery) }))
  await screen.findByText('Your discovered routes')
  expect(screen.getByText('Routes discovered: 1/2')).toBeDefined()
  const forged = { ...discovery, routeId: routeB }
  saveDiscovery(key, forged)
  window.dispatchEvent(new StorageEvent('storage', { key: collectionStoragePrefix(key) + routeB, newValue: JSON.stringify(forged) }))
  expect(screen.getByText('Routes discovered: 1/2')).toBeDefined()
  expect(screen.queryByText(/Route 2 ·/)).toBeNull()
})
