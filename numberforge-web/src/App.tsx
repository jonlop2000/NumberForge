import { useCallback, useEffect, useRef, useState } from 'react'
import MoveHistory from './components/MoveHistory'
import OperationCards from './components/OperationCards'
import RouteCollection from './components/RouteCollection'
import { apiFetch, failureMessage } from './lib/api'
import type { ApplyMoveRequest, DailyPuzzleSetResponse, PuzzleProgressResponse, PuzzleResponse, SessionResponse } from './types/puzzle'
import './App.css'

const savedSessionKey = 'numberforge.sessionId'
const savedPuzzleKey = 'numberforge.puzzleId'

function readSaved(key: string) {
  try { return localStorage.getItem(key) } catch { return null }
}
function save(key: string, value: string | null) {
  try {
    if (value === null) localStorage.removeItem(key)
    else localStorage.setItem(key, value)
  } catch { /* A blocked browser cache must not prevent play. */ }
}
function formatElapsedTime(seconds: number) {
  const total = Math.floor(seconds)
  return `${Math.floor(total / 60)}:${String(total % 60).padStart(2, '0')}`
}

type Catalog = { puzzles: PuzzleResponse[]; daily: DailyPuzzleSetResponse }
type PendingMove = { sessionId: string; request: ApplyMoveRequest }

function App() {
  const [catalog, setCatalog] = useState<Catalog | null>(null)
  const [selectedPuzzleId, setSelectedPuzzleId] = useState(() => readSaved(savedPuzzleKey) ?? '')
  const [session, setSession] = useState<SessionResponse | null>(null)
  const liveSession = useRef<SessionResponse | null>(null)
  const [isRestoring, setIsRestoring] = useState(true)
  const [restoreError, setRestoreError] = useState<string | null>(null)
  const [catalogError, setCatalogError] = useState<string | null>(null)
  const [reloadAttempt, setReloadAttempt] = useState(0)
  const [isStarting, setIsStarting] = useState(false)
  const [startError, setStartError] = useState<string | null>(null)
  const [isMoving, setIsMoving] = useState(false)
  const [moveError, setMoveError] = useState<string | null>(null)
  const [needsRefresh, setNeedsRefresh] = useState(false)
  const [pendingMove, setPendingMove] = useState<PendingMove | null>(null)
  const [progress, setProgress] = useState<PuzzleProgressResponse | null>(null)
  const [progressError, setProgressError] = useState<{ puzzleId: string; message: string } | null>(null)
  const [progressAttempt, setProgressAttempt] = useState(0)
  const [dailyEnded, setDailyEnded] = useState(false)
  const [expiredSessionId, setExpiredSessionId] = useState<string | null>(null)
  const restoreInFlight = useRef(true)
  const startInFlight = useRef(false)
  const moveInFlight = useRef(false)
  // The HTTP Date header keeps the reset aligned with the server if the device clock is wrong.
  const serverClockOffset = useRef(0)

  const puzzle = catalog?.puzzles.find(p => p.id === selectedPuzzleId) ?? catalog?.daily.puzzles[0] ?? catalog?.puzzles[0] ?? null
  const todayIds = new Set(catalog?.daily.puzzles.map(p => p.id) ?? [])
  const practice = catalog?.puzzles.filter(p => !todayIds.has(p.id)) ?? []
  const isCurrentPuzzle = session?.puzzleId != null && catalog?.puzzles.some(p => p.id === session.puzzleId) === true
  const sessionExpired = session?.status === 'expired' || (session !== null && session.sessionId === expiredSessionId)
  const hasWon = session?.status === 'won'
  const isFinished = session !== null && (session.status !== 'active' || sessionExpired)
  const canReplay = isFinished && isCurrentPuzzle && !sessionExpired
  const currentPuzzleId = session?.puzzleId ?? puzzle?.id
  const collectionKey = session?.collectionKey ?? puzzle?.collectionKey
  const visibleProgressError = progressError !== null && progressError.puzzleId === currentPuzzleId ? progressError.message : null
  const visibleProgress = progress !== null && progress.puzzleId === currentPuzzleId && progress.collectionKey === collectionKey ? progress : null

  const acceptSession = useCallback((snapshot: SessionResponse | null) => {
    liveSession.current = snapshot
    setSession(snapshot)
    if (snapshot !== null) {
      save(savedSessionKey, snapshot.sessionId)
      if (snapshot.puzzleId !== null) {
        setSelectedPuzzleId(snapshot.puzzleId)
        save(savedPuzzleKey, snapshot.puzzleId)
      }
    }
  }, [])

  const handleMissingSession = useCallback(() => {
    save(savedSessionKey, null)
    acceptSession(null)
    setPendingMove(null)
    setNeedsRefresh(false)
    setMoveError('This game is no longer available. Choose a puzzle and start a new attempt.')
  }, [acceptSession])

  const requestSync = useCallback(() => {
    if (restoreInFlight.current || startInFlight.current || moveInFlight.current) return false
    restoreInFlight.current = true
    setIsRestoring(true)
    setRestoreError(null)
    setReloadAttempt(attempt => attempt + 1)
    return true
  }, [])

  useEffect(() => {
    const controller = new AbortController()
    restoreInFlight.current = true
    async function restore() {
      // Establish the guest cookie before issuing parallel/owned requests on a first visit.
      try {
        const response = await apiFetch('/api/puzzles', { signal: controller.signal })
        if (!response.ok) throw new Error(failureMessage(response, 'Unable to load puzzles. Please try again.'))
        const all: PuzzleResponse[] = await response.json()
        const dailyResponse = await apiFetch('/api/puzzles/daily', { signal: controller.signal })
        if (!dailyResponse.ok) throw new Error(failureMessage(dailyResponse, 'Unable to load today’s puzzles. Please try again.'))
        const daily: DailyPuzzleSetResponse = await dailyResponse.json()
        if (all.length === 0 || daily.puzzles.length !== 3) throw new Error('The puzzle set is unavailable. Please try again.')
        if (controller.signal.aborted) return
        const serverDate = Date.parse(dailyResponse.headers.get('Date') ?? '')
        if (Number.isFinite(serverDate)) serverClockOffset.current = serverDate - Date.now()
        // These requests can straddle midnight. Keep only the daily endpoint's current set.
        const available = [...all.filter(p => !p.id.startsWith('daily-')), ...daily.puzzles]
        setCatalog({ puzzles: available, daily })
        setCatalogError(null)
        setDailyEnded(Date.parse(daily.expiresAtUtc) <= Date.now() + serverClockOffset.current)
        // Yesterday's selection must not silently remain selected after the daily reset.
        setSelectedPuzzleId(previous => {
          const id = available.some(p => p.id === previous) ? previous : daily.puzzles[0].id
          save(savedPuzzleKey, id)
          return id
        })
      } catch (error) {
        if (controller.signal.aborted) return
        setCatalogError(error instanceof Error ? error.message : 'Unable to load puzzles. Please try again.')
      }
      try {
        const id = liveSession.current?.sessionId ?? readSaved(savedSessionKey)
        if (!id) return
        const response = await apiFetch(`/api/sessions/${encodeURIComponent(id)}`, { signal: controller.signal })
        if (controller.signal.aborted) return
        if (response.status === 404) { handleMissingSession(); return }
        if (!response.ok) throw new Error(failureMessage(response, 'Unable to restore your saved game. Please try again.'))
        const snapshot: SessionResponse = await response.json()
        if (!controller.signal.aborted) {
          acceptSession(snapshot)
          setPendingMove(null)
          setNeedsRefresh(false)
        }
      } catch (error) {
        if (!controller.signal.aborted) setRestoreError(error instanceof Error ? error.message : 'Unable to restore your saved game. Please try again.')
      } finally {
        if (!controller.signal.aborted) {
          restoreInFlight.current = false
          setIsRestoring(false)
        }
      }
    }
    void restore()
    return () => controller.abort()
  }, [reloadAttempt, acceptSession, handleMissingSession])

  useEffect(() => {
    // Returning to the tab refreshes another tab's moves and any newly rotated daily set.
    function onFocus() { if (document.visibilityState !== 'hidden') requestSync() }
    window.addEventListener('focus', onFocus)
    document.addEventListener('visibilitychange', onFocus)
    return () => {
      window.removeEventListener('focus', onFocus)
      document.removeEventListener('visibilitychange', onFocus)
    }
  }, [requestSync])

  useEffect(() => {
    if (!catalog) return
    let timer: ReturnType<typeof setTimeout>
    function rollover() {
      setDailyEnded(true)
      // A finishing request may still be in flight at midnight. Let it settle before restoring.
      if (!requestSync()) timer = setTimeout(rollover, 1000)
    }
    timer = setTimeout(rollover, Math.max(0, Math.min(2_147_483_647,
      Date.parse(catalog.daily.expiresAtUtc) - (Date.now() + serverClockOffset.current) + 100)))
    return () => clearTimeout(timer)
  }, [catalog, requestSync])

  useEffect(() => {
    if (!session?.playUntilUtc) return
    const id = session.sessionId
    const delay = Date.parse(session.playUntilUtc) - (Date.now() + serverClockOffset.current)
    const timer = setTimeout(() => setExpiredSessionId(id), Math.max(0, Math.min(2_147_483_647, delay)))
    return () => clearTimeout(timer)
  }, [session?.sessionId, session?.playUntilUtc, catalog?.daily.expiresAtUtc])

  useEffect(() => {
    if (isRestoring || restoreError || !catalog || !currentPuzzleId || !catalog.puzzles.some(p => p.id === currentPuzzleId)) return
    const controller = new AbortController()
    async function loadProgress() {
      try {
        const response = await apiFetch(`/api/puzzles/${encodeURIComponent(currentPuzzleId!)}/progress`, { signal: controller.signal })
        if (!response.ok) throw new Error(failureMessage(response, 'Unable to load your route collection. Please try again.'))
        const data: PuzzleProgressResponse = await response.json()
        if (!controller.signal.aborted) {
          setProgress(data)
          setProgressError(null)
        }
      } catch (error) {
        if (!controller.signal.aborted) {
          setProgress(null)
          setProgressError({ puzzleId: currentPuzzleId!, message: error instanceof Error ? error.message : 'Unable to load your route collection. Please try again.' })
        }
      }
    }
    void loadProgress()
    return () => controller.abort()
  }, [catalog, currentPuzzleId, session?.revision, isRestoring, restoreError, progressAttempt])

  async function startGame() {
    const id = session?.puzzleId ?? puzzle?.id
    if (!id || restoreInFlight.current || restoreError || startInFlight.current || moveInFlight.current
      || (session !== null && !canReplay) || (session === null && (catalogError || (todayIds.has(id) && dailyEnded)))) return
    startInFlight.current = true
    setIsStarting(true)
    setStartError(null)
    setMoveError(null)
    let rotateDaily = false
    try {
      const response = await apiFetch(`/api/sessions?puzzleId=${encodeURIComponent(id)}`, { method: 'POST' })
      if (response.status === 404 || response.status === 410) {
        setStartError('This puzzle is no longer available. Choose from the current daily set or practice.')
        setDailyEnded(true)
        rotateDaily = true
        return
      }
      if (!response.ok) throw new Error(failureMessage(response, 'Unable to start the game. Please try again.'))
      const created: SessionResponse = await response.json()
      acceptSession(created)
      setPendingMove(null)
      setNeedsRefresh(false)
    } catch (error) {
      setStartError(error instanceof Error ? error.message : 'Unable to start the game. Please try again.')
    } finally {
      startInFlight.current = false
      setIsStarting(false)
      if (rotateDaily) requestSync()
    }
  }

  async function refreshSession(id: string) {
    const response = await apiFetch(`/api/sessions/${encodeURIComponent(id)}`)
    if (response.status === 404) { handleMissingSession(); return null }
    if (!response.ok) throw new Error(failureMessage(response, 'Unable to refresh the game. Please try again.'))
    const latest: SessionResponse = await response.json()
    acceptSession(latest)
    return latest
  }

  async function sendMove(move: PendingMove) {
    if (moveInFlight.current || restoreInFlight.current || startInFlight.current) return
    moveInFlight.current = true
    setIsMoving(true)
    setMoveError(null)
    let rotateDaily = false
    try {
      const response = await apiFetch(`/api/sessions/${encodeURIComponent(move.sessionId)}/moves`, {
        method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(move.request),
      })
      if (response.status === 404) { handleMissingSession(); return }
      if (response.status === 410) {
        setDailyEnded(true)
        setPendingMove(null)
        setNeedsRefresh(true)
        setMoveError('This daily set has ended. Choose a puzzle from today’s set.')
        await refreshSession(move.sessionId)
        rotateDaily = true
        return
      }
      if (response.status === 409) {
        // A stale tab must use the server's snapshot before the player chooses another card.
        setNeedsRefresh(true)
        setPendingMove(null)
        if (await refreshSession(move.sessionId) === null) return
        setNeedsRefresh(false)
        setMoveError('Your game changed in another request. It has been refreshed; check the remaining cards.')
        return
      }
      if (!response.ok) {
        const message = failureMessage(response, 'Unable to confirm this move. Retry the same move or refresh the game.')
        // Validation/rate errors cannot have accepted a move; storage/connection failures are uncertain.
        if (response.status < 500) {
          setPendingMove(null)
          setMoveError(message)
          return
        }
        throw new Error(message)
      }
      const updated: SessionResponse = await response.json()
      acceptSession(updated)
      setPendingMove(null)
      setNeedsRefresh(false)
    } catch (error) {
      setNeedsRefresh(true)
      setMoveError(error instanceof Error ? error.message : 'Unable to confirm this move. Retry the same move or refresh the game.')
    } finally {
      moveInFlight.current = false
      setIsMoving(false)
      if (rotateDaily) requestSync()
    }
  }

  function applyMove(operationId: number) {
    if (!session || isFinished || needsRefresh || pendingMove || restoreInFlight.current || moveInFlight.current || startInFlight.current) return
    const move: PendingMove = {
      sessionId: session.sessionId,
      request: { operationId, expectedRevision: session.revision, requestId: crypto.randomUUID() },
    }
    setPendingMove(move)
    void sendMove(move)
  }

  async function refreshGame() {
    if (!session || moveInFlight.current || restoreInFlight.current) return
    moveInFlight.current = true
    setIsMoving(true)
    setMoveError(null)
    try {
      await refreshSession(session.sessionId)
      setPendingMove(null)
      setNeedsRefresh(false)
      setProgressAttempt(attempt => attempt + 1)
    } catch (error) {
      setNeedsRefresh(true)
      setMoveError(error instanceof Error ? error.message : 'Unable to refresh the game. Please try again.')
    } finally {
      moveInFlight.current = false
      setIsMoving(false)
    }
  }

  function selectPuzzle(id: string) {
    if (session || restoreInFlight.current || startInFlight.current || moveInFlight.current) return
    setSelectedPuzzleId(id)
    save(savedPuzzleKey, id)
    setStartError(null)
    setMoveError(null)
    setProgressError(null)
  }

  function chooseAnotherPuzzle() {
    if (!isFinished || startInFlight.current || moveInFlight.current || restoreInFlight.current) return
    save(savedSessionKey, null)
    acceptSession(null)
    setPendingMove(null)
    setNeedsRefresh(false)
    setStartError(null)
    setMoveError(null)
    if (puzzle) { setSelectedPuzzleId(puzzle.id); save(savedPuzzleKey, puzzle.id) }
    if (dailyEnded || !isCurrentPuzzle) requestSync()
  }

  const winMessage = session?.isPerfect === true ? 'Perfect! You used the fewest possible cards.'
    : session?.isPerfect === false ? 'Target reached! There’s a shorter route to discover.' : 'Target reached!'
  const hasDisplayData = !isRestoring && !restoreError && (session !== null || (puzzle !== null && !catalogError))

  return (
    <main id="center">
      <h1>Number Forge</h1>
      <p className="game-feedback">Reach the target using as few cards as you can. Each card can be used once.</p>
      {catalog && <p className="game-feedback">Daily set: {catalog.daily.date} · Resets at midnight UTC.</p>}

      {!session && catalog && !isRestoring && !restoreError && !catalogError && (
        <div className="puzzle-picker">
          <label htmlFor="puzzle-select">Choose a puzzle</label>
          <select id="puzzle-select" value={puzzle?.id ?? ''} onChange={event => selectPuzzle(event.target.value)} disabled={isStarting}>
            <optgroup label="Today">
              {catalog.daily.puzzles.map(item => <option key={item.id} value={item.id}>{item.name} · {item.maxMoves} moves</option>)}
            </optgroup>
            <optgroup label="Practice">
              {practice.map(item => <option key={item.id} value={item.id}>{item.name}</option>)}
            </optgroup>
          </select>
        </div>
      )}

      {isRestoring ? <p role="status">Checking for a saved game…</p>
        : restoreError ? <p className="game-feedback" role="alert">{restoreError}</p>
        : session ? (
          <>
            <h2>{session.puzzleName ?? 'Your game'}</h2>
            <p role="status">{hasWon ? winMessage
              : sessionExpired ? 'This daily set has ended. Choose a puzzle from today’s set.'
              : isFinished ? 'Game over. You ran out of moves or cards before reaching the target.'
              : isMoving ? 'Updating game…' : needsRefresh ? 'Confirm or refresh your move to continue.' : 'Choose an operation card.'}</p>
            <p>Current number: {session.currentNumber}</p>
            <p>Target: {session.targetNumber}</p>
            <p>Moves used: {session.movesUsed}/{session.maxMoves}</p>
            {isFinished && typeof session.elapsedSeconds === 'number' && (
              <p className="game-result">{hasWon ? 'Solved in' : 'Attempt ended after'} {session.movesUsed} {session.movesUsed === 1 ? 'card' : 'cards'} · {formatElapsedTime(session.elapsedSeconds)}</p>
            )}
            {session.charm && (
              <section aria-labelledby="charm-heading">
                <h2 id="charm-heading">{session.charm.name} {session.charm.isUsed ? 'used' : 'ready'}</h2>
                <p>{session.charm.description}</p>
              </section>
            )}
          </>
        ) : catalogError ? <p role="alert">{catalogError}</p>
          : puzzle ? <><h2>{puzzle.name}</h2><p>Starting number: {puzzle.startNumber}</p><p>Target: {puzzle.targetNumber}</p><p>Move limit: {puzzle.maxMoves}</p></>
            : <p role="status">Loading puzzle…</p>}

      {!isRestoring && session && catalogError && <p className="game-feedback" role="alert">{catalogError}</p>}
      {!isRestoring && (restoreError || catalogError) && (
        <button type="button" className="refresh-game" onClick={requestSync}>Retry loading</button>
      )}
      {hasDisplayData && (
        <OperationCards operations={session?.availableOperations ?? puzzle?.operations ?? []}
          disabled={isMoving || isStarting || isFinished || needsRefresh || pendingMove !== null}
          isBusy={isMoving} onSelect={session !== null ? applyMove : undefined} />
      )}
      {session && <MoveHistory sessionId={session.sessionId} history={session.history} />}
      {hasDisplayData && visibleProgress && (!session || isCurrentPuzzle) && (
        <RouteCollection collectionKey={visibleProgress.collectionKey} progress={visibleProgress} session={session} />
      )}
      {visibleProgressError && !isRestoring && (!session || isCurrentPuzzle) && (
        <><p className="game-feedback" role="alert">{visibleProgressError}</p>
          <button type="button" className="refresh-game" onClick={() => setProgressAttempt(attempt => attempt + 1)}>Retry progress</button></>
      )}
      {moveError && <p className="game-feedback" role="alert">{moveError}</p>}
      {session && needsRefresh && (
        <>
          {pendingMove && !sessionExpired && <button type="button" className="refresh-game" onClick={() => void sendMove(pendingMove)} disabled={isMoving}>Retry move</button>}
          <button type="button" className="refresh-game" onClick={refreshGame} disabled={isMoving}>{isMoving ? 'Refreshing…' : 'Refresh game'}</button>
        </>
      )}
      {!isRestoring && !restoreError && ((!session && puzzle && !catalogError) || canReplay) && (
        <button type="button" className="start-game" onClick={startGame}
          disabled={isStarting || isMoving || (!session && todayIds.has(puzzle?.id ?? '') && dailyEnded)} aria-busy={isStarting}>
          {isStarting ? 'Starting…' : session ? 'Play again' : 'Start game'}
        </button>
      )}
      {isFinished && !isRestoring && (
        <button type="button" className="choose-puzzle" onClick={chooseAnotherPuzzle} disabled={isStarting || isMoving}>Choose another puzzle</button>
      )}
      {startError && <p className="game-feedback" role="alert">{startError}</p>}
    </main>
  )
}

export default App
