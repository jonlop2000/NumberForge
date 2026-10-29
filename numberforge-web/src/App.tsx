import { useEffect, useRef, useState } from 'react'
import type { PuzzleResponse, SessionResponse } from './types/puzzle'
import './App.css'

function App() {
  const [puzzle, setPuzzle] = useState<PuzzleResponse | null>(null)
  const [error, setError] = useState<string | null>(null)

  const [session, setSession] = useState<SessionResponse | null>(null)
  const [isStarting, setIsStarting] = useState(false)
  const [startError, setStartError] = useState<string | null>(null)

  const [isMoving, setIsMoving] = useState(false)
  const [moveError, setMoveError] = useState<string | null>(null)
  const [needsRefresh, setNeedsRefresh] = useState(false)

  const startInFlight = useRef(false)
  const moveInFlight = useRef(false)

  const hasWon = session !== null && session.currentNumber === session.targetNumber
  const isFinished = session !== null && (
    hasWon || session.movesUsed >= session.maxMoves || session.availableOperations.length === 0
  )

  useEffect(() => {
    const controller = new AbortController()

    async function loadPuzzle() {
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

    void loadPuzzle()
    return () => controller.abort()
  }, [])

  async function startGame() {
    // Allow the first game and replays after a win or loss, while protecting an active game.
    // The refs also block rapid clicks before React renders the disabled button.
    if (startInFlight.current || moveInFlight.current || (session !== null && !isFinished)) {
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
    } catch {
      setStartError('Unable to start the game. Please try again.')
    } finally {
      startInFlight.current = false
      setIsStarting(false)
    }
  }

  function handleMissingSession() {
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
  const hasDisplayData = session !== null || (puzzle !== null && error === null)

  return (
    <main id="center">
      <h1>Number Forge</h1>

      {/* Once a game starts, its response becomes the source of the displayed progress. */}
      {session !== null ? (
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

      {hasDisplayData && (
        <section aria-labelledby="operations-heading">
          <h2 id="operations-heading">Available operations</h2>
          <ul className="operation-list" aria-busy={isMoving}>
            {/* The preview and session both provide cards with stable IDs. */}
            {displayedOperations.map((operation) => (
              <li key={operation.id}>
                {session !== null ? (
                  <button
                    type="button"
                    className="operation-card"
                    onClick={() => applyMove(operation.id)}
                    disabled={isMoving || isFinished || needsRefresh}
                  >
                    {operation.description}
                  </button>
                ) : operation.description}
              </li>
            ))}
          </ul>
          {session !== null && displayedOperations.length === 0 && <p>No cards remaining.</p>}
        </section>
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
      {((session === null && puzzle !== null && error === null) || isFinished) && (
        <button
          type="button"
          className="start-game"
          onClick={startGame}
          disabled={isStarting || isMoving}
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
