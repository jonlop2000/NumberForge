import { StrictMode } from 'react'
import { act, cleanup, fireEvent, render, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, expect, test, vi } from 'vitest'
import type { ApplyMoveRequest, DailyPuzzleSetResponse, PuzzleProgressResponse, PuzzleResponse, SessionResponse } from './types/puzzle'
import App from './App'

const featured: PuzzleResponse = {
  id: 'demo', name: 'First Forge', startNumber: 3, targetNumber: 14, maxMoves: 2,
  collectionKey: 'a'.repeat(64), totalRoutes: null,
  operations: [
    { id: 1, description: 'Add 2', previewNumber: null },
    { id: 2, description: 'Multiply by 2', previewNumber: null },
    { id: 3, description: 'Add 4', previewNumber: null },
  ],
}
function dailySet(date = '2099-01-01'): DailyPuzzleSetResponse {
  const start = new Date(date + 'T00:00:00Z')
  return {
    date, availableFromUtc: start.toISOString(), expiresAtUtc: new Date(start.getTime() + 86400000).toISOString(),
    puzzles: [3, 4, 5].map((maxMoves, index) => ({ ...featured,
      id: `daily-v1-${date.replaceAll('-', '')}-${index + 1}`, name: `Daily Forge ${index + 1}`,
      collectionKey: String(index + 1).repeat(64), maxMoves,
    })),
  }
}
const daily = dailySet()
function sessionFor(puzzle = featured, sessionId = 'attempt'): SessionResponse {
  return {
    sessionId, puzzleId: puzzle.id, puzzleName: puzzle.name,
    currentNumber: puzzle.startNumber, targetNumber: puzzle.targetNumber, revision: 0,
    movesUsed: 0, maxMoves: puzzle.maxMoves, availableOperations: puzzle.operations,
    collectionKey: puzzle.collectionKey, totalRoutes: null, status: 'active',
    playUntilUtc: puzzle.id.startsWith('daily-') ? daily.expiresAtUtc : null,
    expiresAtUtc: '2099-01-03T00:00:00Z',
    history: [], charm: null, routeId: null, elapsedSeconds: null, isPerfect: null,
  }
}
function afterFirst(): SessionResponse {
  return { ...sessionFor(), revision: 1, currentNumber: 7, movesUsed: 1,
    availableOperations: featured.operations.slice(1),
    history: [{ beforeNumber: 3, operationDescription: 'Add 2 (Echo ×2)', afterNumber: 7 }] }
}
function winning(): SessionResponse {
  return { ...afterFirst(), revision: 2, status: 'won', currentNumber: 14, movesUsed: 2,
    totalRoutes: 2, routeId: 'c'.repeat(64), isPerfect: true, elapsedSeconds: 12,
    availableOperations: [featured.operations[2]],
    history: [...afterFirst().history, { beforeNumber: 7, operationDescription: 'Multiply by 2', afterNumber: 14 }] }
}
function emptyProgress(puzzle: PuzzleResponse): PuzzleProgressResponse {
  return { puzzleId: puzzle.id, collectionKey: puzzle.collectionKey, hasSolved: false,
    totalRoutes: null, foundRoutes: 0, bestMoves: null, routeIds: [] }
}
function wonProgress(): PuzzleProgressResponse {
  return { ...emptyProgress(featured), hasSolved: true, totalRoutes: 2, foundRoutes: 1,
    bestMoves: 2, routeIds: ['c'.repeat(64)] }
}
function json(data: unknown, status = 200, headers: HeadersInit = {}) {
  return new Response(JSON.stringify(data), { status, headers: { 'Content-Type': 'application/json', ...headers } })
}
type Handler = (url: string, init?: RequestInit) => Promise<Response | undefined> | Response | undefined
function mockApi(handler: Handler = () => undefined) {
  const api = vi.fn(async (input: RequestInfo | URL, init?: RequestInit): Promise<Response> => {
    const url = String(input)
    const custom = await handler(url, init)
    if (custom) return custom
    if (url === '/api/puzzles') return json([featured, ...daily.puzzles])
    if (url === '/api/puzzles/daily') return json(daily)
    const puzzle = [featured, ...daily.puzzles].find(p => url === `/api/puzzles/${p.id}/progress`)
    if (puzzle) return json(emptyProgress(puzzle))
    if (url.startsWith('/api/sessions?puzzleId=') && init?.method === 'POST') {
      const selected = [featured, ...daily.puzzles].find(p => url.endsWith(p.id))!
      return json(sessionFor(selected), 201)
    }
    throw new Error('Unexpected request: ' + url)
  })
  vi.stubGlobal('fetch', api)
  return api
}
function restore(snapshot = sessionFor()) {
  localStorage.setItem('numberforge.sessionId', snapshot.sessionId)
  return snapshot
}
afterEach(() => { cleanup(); vi.unstubAllGlobals(); vi.restoreAllMocks(); vi.useRealTimers(); localStorage.clear() })

test('defaults to the daily set, groups today and practice, and never calls retired generation', async () => {
  const api = mockApi()
  render(<StrictMode><App /></StrictMode>)
  await screen.findByRole('heading', { name: 'Daily Forge 1' })
  const picker = screen.getByRole('combobox')
  expect(within(picker).getByRole('group', { name: 'Today' }).querySelectorAll('option')).toHaveLength(3)
  expect(within(picker).getByRole('group', { name: 'Practice' }).textContent).toContain('First Forge')
  expect(screen.getByText('Move limit: 3')).toBeDefined()
  expect(screen.queryByRole('button', { name: 'New challenge' })).toBeNull()
  expect(screen.queryByText(/Routes discovered:/)).toBeNull()
  expect(api.mock.calls.every(([, init]) => init?.credentials === 'same-origin')).toBe(true)
  expect(api.mock.calls.some(([url]) => String(url).includes('/generated'))).toBe(false)
})

test('switches to practice and prevents duplicate session creation', async () => {
  const user = userEvent.setup()
  let release!: (response: Response) => void
  const pending = new Promise<Response>(resolve => { release = resolve })
  const api = mockApi((url, init) => url === '/api/sessions?puzzleId=demo' && init?.method === 'POST' ? pending : undefined)
  render(<App />)
  await screen.findByRole('combobox')
  await user.selectOptions(screen.getByRole('combobox'), 'demo')
  await user.dblClick(screen.getByRole('button', { name: 'Start game' }))
  expect(screen.getByRole('combobox').hasAttribute('disabled')).toBe(true)
  expect(api.mock.calls.filter(([url]) => url === '/api/sessions?puzzleId=demo')).toHaveLength(1)
  release(json(sessionFor(), 201))
  await screen.findByText('Moves used: 0/2')
  expect(localStorage.getItem('numberforge.sessionId')).toBe('attempt')
})

test('replaces yesterday’s saved picker selection with today’s first puzzle', async () => {
  localStorage.setItem('numberforge.puzzleId', 'daily-v1-20981231-1')
  mockApi()
  render(<App />)
  await screen.findByRole('heading', { name: 'Daily Forge 1' })
  expect((screen.getByRole('combobox') as HTMLSelectElement).value).toBe(daily.puzzles[0].id)
  expect(localStorage.getItem('numberforge.puzzleId')).toBe(daily.puzzles[0].id)
})

test('wins using revisioned moves, unlocks verified progress, then replays with the collection intact', async () => {
  const user = userEvent.setup()
  restore()
  let current = sessionFor()
  let hasWon = false
  const api = mockApi((url, init) => {
    if (url === '/api/sessions/attempt' && init?.method !== 'POST') return json(current)
    if (url === '/api/sessions/attempt/moves') {
      const request: ApplyMoveRequest = JSON.parse(String(init?.body))
      current = request.operationId === 1 ? afterFirst() : winning()
      hasWon = current.status === 'won'
      return json(current)
    }
    if (url === '/api/puzzles/demo/progress') return json(hasWon ? wonProgress() : emptyProgress(featured))
    if (url === '/api/sessions?puzzleId=demo') return json(sessionFor(featured, 'replay'), 201)
  })
  render(<App />)
  await screen.findByText('Moves used: 0/2')
  expect(screen.queryByText(/Solved in/)).toBeNull()
  await user.click(screen.getByRole('button', { name: 'Add 2' }))
  await screen.findByText('Current number: 7')
  await user.click(screen.getByRole('button', { name: 'Multiply by 2' }))
  await screen.findByText('Solved in 2 cards · 0:12')
  await screen.findByText('Routes discovered: 1/2')
  const moves = api.mock.calls.filter(([url]) => String(url).endsWith('/moves')).map(([, init]) => JSON.parse(String(init?.body)))
  expect(moves.map(m => m.expectedRevision)).toEqual([0, 1])
  expect(moves[0].requestId).not.toBe(moves[1].requestId)
  expect(moves.every(m => /^[0-9a-f-]{36}$/.test(m.requestId))).toBe(true)
  await user.click(screen.getByRole('button', { name: 'Play again' }))
  await screen.findByText('Moves used: 0/2')
  expect(screen.getByText('Routes discovered: 1/2')).toBeDefined()
  expect(screen.queryByText(/Solved in/)).toBeNull()
})

test.each(['network', 'server'])('retries an uncertain %s failure with the identical request ID and revision', async failure => {
  const user = userEvent.setup()
  restore()
  let posts = 0
  const api = mockApi((url) => {
    if (url === '/api/sessions/attempt') return json(sessionFor())
    if (url.endsWith('/moves')) {
      if (++posts === 1) {
        if (failure === 'network') throw new TypeError('Connection lost')
        return json({}, 503)
      }
      return json(afterFirst())
    }
    return undefined
  })
  render(<App />)
  await screen.findByText('Moves used: 0/2')
  await user.click(screen.getByRole('button', { name: 'Add 2' }))
  await screen.findByRole('button', { name: 'Retry move' })
  expect(screen.getByRole('button', { name: 'Multiply by 2' }).hasAttribute('disabled')).toBe(true)
  await user.click(screen.getByRole('button', { name: 'Retry move' }))
  await screen.findByText('Moves used: 1/2')
  const moves = api.mock.calls.filter(([url]) => String(url).endsWith('/moves'))
  expect(moves).toHaveLength(2)
  expect(moves[0][1]?.body).toBe(moves[1][1]?.body)
  expect(screen.queryByRole('button', { name: 'Retry move' })).toBeNull()
})

test('refreshes a conflicting revision before allowing another choice', async () => {
  const user = userEvent.setup()
  restore()
  let conflict = false
  mockApi((url) => {
    if (url === '/api/sessions/attempt') return json(conflict ? afterFirst() : sessionFor())
    if (url.endsWith('/moves')) { conflict = true; return json({ title: 'Move rejected' }, 409) }
  })
  render(<App />)
  await screen.findByText('Moves used: 0/2')
  await user.click(screen.getByRole('button', { name: 'Add 2' }))
  await screen.findByText('Moves used: 1/2')
  expect(screen.queryByRole('button', { name: 'Add 2' })).toBeNull()
  expect(screen.getByRole('button', { name: 'Multiply by 2' }).hasAttribute('disabled')).toBe(false)
  expect(screen.getByRole('alert').textContent).toContain('game changed')
})

test('honors rate-limit errors without claiming a move was accepted or auto-retrying', async () => {
  const user = userEvent.setup()
  restore()
  const api = mockApi((url) => {
    if (url === '/api/sessions/attempt') return json(sessionFor())
    if (url.endsWith('/moves')) return json({}, 429, { 'Retry-After': '12' })
  })
  render(<App />)
  await screen.findByText('Moves used: 0/2')
  await user.click(screen.getByRole('button', { name: 'Add 2' }))
  await screen.findByText('Too many requests. Try again in 12 seconds.')
  expect(screen.getByText('Moves used: 0/2')).toBeDefined()
  expect(api.mock.calls.filter(([url]) => String(url).endsWith('/moves'))).toHaveLength(1)
  expect(screen.queryByRole('button', { name: 'Retry move' })).toBeNull()
})

test('clears a missing saved session and returns to the current selector', async () => {
  restore()
  mockApi(url => url === '/api/sessions/attempt' ? json(null, 404) : undefined)
  render(<App />)
  await screen.findByRole('combobox')
  expect(localStorage.getItem('numberforge.sessionId')).toBeNull()
  expect(screen.getByRole('button', { name: 'Start game' })).toBeDefined()
})

test('expired daily attempts disable cards and replay while allowing a current puzzle', async () => {
  const user = userEvent.setup()
  const expired = restore({ ...sessionFor(daily.puzzles[0]), status: 'expired' })
  mockApi(url => url === '/api/sessions/attempt' ? json(expired) : undefined)
  render(<App />)
  await screen.findByText(/This daily set has ended/)
  expect(screen.getByRole('button', { name: 'Add 2' }).hasAttribute('disabled')).toBe(true)
  expect(screen.queryByRole('button', { name: 'Play again' })).toBeNull()
  await user.click(screen.getByRole('button', { name: 'Choose another puzzle' }))
  await screen.findByRole('combobox')
  expect(screen.getByRole('button', { name: 'Start game' })).toBeDefined()
})

test('yesterday’s completed result remains readable but cannot be replayed', async () => {
  const completed = restore({ ...winning(), puzzleId: 'daily-v1-20981231-1', puzzleName: 'Yesterday’s Forge', playUntilUtc: '2000-01-01T00:00:00Z' })
  mockApi(url => url === '/api/sessions/attempt' ? json(completed) : undefined)
  render(<App />)
  await screen.findByText('Solved in 2 cards · 0:12')
  expect(screen.queryByRole('button', { name: 'Play again' })).toBeNull()
  expect(screen.queryByText(/Routes discovered:/)).toBeNull()
  expect(screen.getByRole('button', { name: 'Choose another puzzle' })).toBeDefined()
})

test('an expiry response refreshes the daily set and preserves the expired attempt until left', async () => {
  const user = userEvent.setup()
  restore(sessionFor(daily.puzzles[0]))
  const next = dailySet('2099-01-02')
  let ended = false
  mockApi(url => {
    if (url === '/api/sessions/attempt') return json({ ...sessionFor(daily.puzzles[0]), status: ended ? 'expired' : 'active' })
    if (url.endsWith('/moves')) { ended = true; return json({}, 410) }
    if (ended && url === '/api/puzzles') return json([featured, ...next.puzzles])
    if (ended && url === '/api/puzzles/daily') return json(next)
    if (ended && url.endsWith('/progress')) return json(emptyProgress(next.puzzles[0]))
  })
  render(<App />)
  await screen.findByText('Moves used: 0/3')
  await user.click(screen.getByRole('button', { name: 'Add 2' }))
  await screen.findByText('Daily set: 2099-01-02 · Resets at midnight UTC.')
  await screen.findByRole('button', { name: 'Choose another puzzle' })
  await user.click(screen.getByRole('button', { name: 'Choose another puzzle' }))
  await screen.findByRole('combobox')
  expect((screen.getByRole('combobox') as HTMLSelectElement).value).toBe(next.puzzles[0].id)
})

test('a progress failure does not lose a won game and can be retried', async () => {
  const user = userEvent.setup()
  restore(winning())
  let online = false
  mockApi(url => {
    if (url === '/api/sessions/attempt') return json(winning())
    if (url === '/api/puzzles/demo/progress') return online ? json(wonProgress()) : json({}, 503)
  })
  render(<App />)
  await screen.findByText('Solved in 2 cards · 0:12')
  await screen.findByRole('button', { name: 'Retry progress' })
  online = true
  await user.click(screen.getByRole('button', { name: 'Retry progress' }))
  await screen.findByText('Routes discovered: 1/2')
  expect(screen.queryByRole('button', { name: 'Retry progress' })).toBeNull()
})

test('failed catalog loading can be retried without losing a saved result', async () => {
  const user = userEvent.setup()
  restore(winning())
  let online = false
  mockApi(url => {
    if (url === '/api/puzzles' && !online) return json({}, 503)
    if (url === '/api/sessions/attempt') return json(winning())
  })
  render(<App />)
  await screen.findByText('Solved in 2 cards · 0:12')
  online = true
  await user.click(screen.getByRole('button', { name: 'Retry loading' }))
  await screen.findByRole('button', { name: 'Play again' })
  expect(localStorage.getItem('numberforge.sessionId')).toBe('attempt')
})

test('returning to the tab restores moves made in another tab', async () => {
  restore()
  let current = sessionFor()
  mockApi(url => url === '/api/sessions/attempt' ? json(current) : undefined)
  render(<App />)
  await screen.findByText('Moves used: 0/2')
  current = afterFirst()
  fireEvent(window, new Event('focus'))
  await screen.findByText('Moves used: 1/2')
})

test('automatically replaces the three daily choices at UTC midnight', async () => {
  vi.useFakeTimers()
  vi.setSystemTime(new Date('2099-01-01T23:59:59.900Z'))
  const next = dailySet('2099-01-02')
  mockApi(url => {
    const set = Date.now() >= Date.parse(daily.expiresAtUtc) ? next : daily
    if (url === '/api/puzzles') return json([featured, ...set.puzzles])
    if (url === '/api/puzzles/daily') return json(set)
    if (url.endsWith('/progress')) return json(emptyProgress(set.puzzles[0]))
  })
  await act(async () => { render(<App />) })
  expect(screen.getByText('Daily set: 2099-01-01 · Resets at midnight UTC.')).toBeDefined()
  await act(async () => { await vi.advanceTimersByTimeAsync(300) })
  expect(screen.getByText('Daily set: 2099-01-02 · Resets at midnight UTC.')).toBeDefined()
  expect((screen.getByRole('combobox') as HTMLSelectElement).value).toBe(next.puzzles[0].id)
  expect(within(screen.getByRole('group', { name: 'Today' })).getAllByRole('option')).toHaveLength(3)
})


test('catalog reads that straddle midnight keep only the current three daily puzzles', async () => {
  const next = dailySet('2099-01-02')
  mockApi(url => url === '/api/puzzles/daily' ? json(next)
    : url.endsWith('/progress') ? json(emptyProgress(next.puzzles[0])) : undefined)
  render(<App />)
  await screen.findByRole('combobox')
  expect(within(screen.getByRole('group', { name: 'Today' })).getAllByRole('option')).toHaveLength(3)
  expect(within(screen.getByRole('group', { name: 'Practice' })).getAllByRole('option')).toHaveLength(1)
  expect(screen.getAllByRole('option')).toHaveLength(4)
  expect((screen.getByRole('combobox') as HTMLSelectElement).value).toBe(next.puzzles[0].id)
})

test('a stale start selection refreshes today’s catalog instead of leaving Start disabled', async () => {
  const user = userEvent.setup()
  const next = dailySet('2099-01-02')
  let ended = false
  mockApi((url, init) => {
    if (url.startsWith('/api/sessions?') && init?.method === 'POST') { ended = true; return json({}, 404) }
    if (ended && url === '/api/puzzles') return json([featured, ...next.puzzles])
    if (ended && url === '/api/puzzles/daily') return json(next)
    if (ended && url.endsWith('/progress')) return json(emptyProgress(next.puzzles[0]))
  })
  render(<App />)
  await screen.findByRole('combobox')
  await user.click(screen.getByRole('button', { name: 'Start game' }))
  await screen.findByText('Daily set: 2099-01-02 · Resets at midnight UTC.')
  expect(screen.getByRole('button', { name: 'Start game' }).hasAttribute('disabled')).toBe(false)
  expect((screen.getByRole('combobox') as HTMLSelectElement).value).toBe(next.puzzles[0].id)
})
