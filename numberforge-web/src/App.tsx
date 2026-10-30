import { useEffect, useRef, useState } from 'react'
import MoveHistory from './components/MoveHistory'
import OperationCards from './components/OperationCards'
import type { PuzzleResponse, SessionResponse } from './types/puzzle'
import './App.css'

const savedSessionKey = 'numberforge.sessionId'

function clearSavedSession() {
  try {
    localStorage.removeItem(savedSessionKey)
  } catch {
  }
}

function App() {
  const [puzzle, setPuzzle] = useState<PuzzleResponse | null>(null)
  const [error, setError] = useState<string | null>(null)

  const [session, setSession] = useState<SessionResponse | null>(null)
  const [isRestoring, setIsRestoring] = useState(true)
  const [restoreError, setRestoreError] = useState<string | null>(null)
  const [restoreAttempt, setRestoreAttempt] = useState(0)
  const [isStarting, setIsStarting] = useState(false)
  const [startError, setStartError] = useState<string | null>(null)

  const [isMoving, setIsMoving] = useState(false)
  const [moveError, setMoveError] = useState<string | null>(null)
  const [needsRefresh, setNeedsRefresh] = useState(false)

  const startInFlight = useRef(false)
  const moveInFlight = useRef(false)
  const restoreInFlight = useRef(true)

  const hasWon = session !== null && session.currentNumber === session.targetNumber
  const isFinished = session !== null && (
    hasWon || session.movesUsed >= session.maxMoves || session.availableOperations.length === 0
  )

  useEffect(() => {
    const controller = new AbortController()
    restoreInFlight.current = true

    async function loadPuzzle() {
      setError(null)
      try {
        const response = await fetch('/api/puzzles/demo', {
          signal: controller.signal,
        })

        if (!response.ok) {
          throw new Error('Failed to fetch puzzle')
        }

        const data: PuzzleResponse = await response.json()

        if (!controller.signal.aborted) {
          setPuzzle(data)
        }
      } catch {

        if (!controller.signal.aborted) {
          setError('Unable to load the puzzle. Please refresh the page to try again.')
        }
      }
    }

    async function restoreSavedSession() {
      try {
        let savedSessionId: string | null = null
        try {
          savedSessionId = localStorage.getItem(savedSessionKey)
        } catch {
          // A browser that blocks storage can still load the preview and start a game.
        }

        if (!savedSessionId) {
          return
        }

        // Restore with GET: loading or retrying the page must never create a new game.
        const response = await fetch(`/api/sessions/${encodeURIComponent(savedSessionId)}`, {
          signal: controller.signal,
        })

        if (controller.signal.aborted) {
          return
        }
        if (response.status === 404) {
          clearSavedSession()
          setMoveError('This game is no longer available. Start a new game.')
          return
        }
        if (!response.ok) {
          throw new Error('Failed to restore game')
        }

        const restoredSession: SessionResponse = await response.json()
        if (!controller.signal.aborted) {
          setSession(restoredSession)
        }
      } catch {
        if (!controller.signal.aborted) {
          setRestoreError('Unable to restore your saved game. Please try again.')
        }
      } finally {
        if (!controller.signal.aborted) {
          restoreInFlight.current = false
          setIsRestoring(false)
        }
      }
    }

    void loadPuzzle()
    void restoreSavedSession()
    return () => controller.abort()
  }, [restoreAttempt])

  function retryRestoration() {
    if (restoreInFlight.current) {
      return
    }

    restoreInFlight.current = true
    setIsRestoring(true)
    setRestoreError(null)
    setRestoreAttempt(attempt => attempt + 1)
  }

  async function startGame() {

    if (restoreInFlight.current || restoreError !== null || startInFlight.current
      || moveInFlight.current || (session !== null && !isFinished)) {
      return
    }

    startInFlight.current = true
    setIsStarting(true)
    setStartError(null)
    setMoveError(null)
    setNeedsRefresh(false)

    try {
      const response = await fetch('/api/sessions', { method: 'POST' })
      if (!response.ok) {
        throw new Error('Failed to start game')
      }

      const createdSession: SessionResponse = await response.json()

      setSession(createdSession)

      try {
        // Save the ID after creation succeeds; Play again replaces it with the new session's ID.
        localStorage.setItem(savedSessionKey, createdSession.sessionId)
      } catch {
        // Browser settings can block storage. The successfully created game is still playable.
      }
    } catch {
      setStartError('Unable to start the game. Please try again.')
    } finally {
      startInFlight.current = false
      setIsStarting(false)
    }
  }

  function handleMissingSession() {
    clearSavedSession()
    setSession(null)
    setNeedsRefresh(false)
    setMoveError('This game is no longer available. Start a new game.')
  }

  async function refreshSession(sessionId: string): Promise<boolean> {
    const response = await fetch(`/api/sessions/${sessionId}`)
    if (response.status === 404) {
      handleMissingSession()
      return false
    }
    if (!response.ok) {
      throw new Error('Failed to refresh game')
    }

    const latestSession: SessionResponse = await response.json()
    setSession(latestSession)
    setNeedsRefresh(false)
    return true
  }

  async function applyMove(operationId: number) {
    if (session === null || moveInFlight.current || isFinished || needsRefresh) {
      return
    }

    const sessionId = session.sessionId
    moveInFlight.current = true
    setIsMoving(true)
    setMoveError(null)

    try {
      const response = await fetch(`/api/sessions/${sessionId}/moves`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ operationId }),
      })

      if (response.status === 404) {
        handleMissingSession()
        return
      }
      if (!response.ok) {
        throw new Error('Failed to apply move')
      }

      const updatedSession: SessionResponse = await response.json()
      setSession(updatedSession)
    } catch {
      try {
        if (await refreshSession(sessionId)) {
          setMoveError('The move could not be confirmed. Your game has been refreshed; check the number and remaining cards before choosing again.')
        }
      } catch {
        setNeedsRefresh(true)
        setMoveError('Unable to confirm the move. Refresh the game before choosing another card.')
      }
    } finally {
      moveInFlight.current = false
      setIsMoving(false)
    }
  }

  async function refreshGame() {
    if (session === null || moveInFlight.current) {
      return
    }

    moveInFlight.current = true
    setIsMoving(true)
    setMoveError(null)

    try {
      await refreshSession(session.sessionId)
    } catch {
      setNeedsRefresh(true)
      setMoveError('Unable to refresh the game. Please try again.')
    } finally {
      moveInFlight.current = false
      setIsMoving(false)
    }
  }

  const displayedOperations = session?.availableOperations ?? puzzle?.operations ?? []
  const hasDisplayData = !isRestoring && restoreError === null
    && (session !== null || (puzzle !== null && error === null))

  return (
    <main id="center">
      <h1>Number Forge</h1>

      {/* Once a game starts, its response becomes the source of the displayed progress. */}
      {isRestoring ? (
        <p role="status">Checking for a saved game…</p>
      ) : restoreError !== null ? (
        <p className="game-feedback" role="alert">{restoreError}</p>
      ) : session !== null ? (
        <>
          {/* A final move can both use the last card and win, so show the win message first. */}
          <p role="status">
            {hasWon ? 'You won! You reached the target.'
              : isFinished ? 'Game over. You ran out of moves or cards before reaching the target.'
              : isMoving ? 'Updating game…'
              : needsRefresh ? 'Refresh your game to continue.'
              : 'Choose an operation card.'}
          </p>
          <p>Current number: {session.currentNumber}</p>
          <p>Target: {session.targetNumber}</p>
          <p>Moves used: {session.movesUsed}/{session.maxMoves}</p>
        </>
      ) : error ? (
        <p role="alert">{error}</p>
      ) : puzzle === null ? (
        <p role="status">Loading puzzle…</p>
      ) : (
        <>
          <p>Starting number: {puzzle.startNumber}</p>
          <p>Target: {puzzle.targetNumber}</p>
          <p>Move limit: {puzzle.maxMoves}</p>
        </>
      )}

      {restoreError !== null && (
        <button type="button" className="refresh-game" onClick={retryRestoration}>
          Retry restore
        </button>
      )}

      {hasDisplayData && (
        <OperationCards
          operations={displayedOperations}
          disabled={isMoving || isFinished || needsRefresh}
          isBusy={isMoving}
          // Passing a callback enables card buttons; leaving it undefined displays the preview.
          onSelect={session !== null ? applyMove : undefined}
        />
      )}

      {/* Pass the current snapshot to the display component after moves, refreshes, and replays. */}
      {session !== null && (
        <MoveHistory sessionId={session.sessionId} history={session.history} />
      )}

      {/* Keep errors visible even if a missing session returns the page to its preview. */}
      {moveError && <p className="game-feedback" role="alert">{moveError}</p>}
      {session !== null && needsRefresh && (
        <button
          type="button"
          className="refresh-game"
          onClick={refreshGame}
          disabled={isMoving}
        >
          {isMoving ? 'Refreshing…' : 'Refresh game'}
        </button>
      )}

      {/* Reuse the start handler for a finished game; an active game has no restart button. */}
      {((session === null && puzzle !== null && error === null && restoreError === null) || isFinished) && (
        <button
          type="button"
          className="start-game"
          onClick={startGame}
          disabled={isStarting || isMoving || isRestoring}
          aria-busy={isStarting}
        >
          {isStarting ? 'Starting…' : isFinished ? 'Play again' : 'Start game'}
        </button>
      )}

      {/* Both the first start and a replay can fail, so show this outside the preview-only branch. */}
      {startError && <p className="game-feedback" role="alert">{startError}</p>}
    </main>
  )
}

export default App
