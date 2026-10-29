import { useEffect, useState } from 'react'
import type { PuzzleResponse } from './types/puzzle'
import './App.css'

function App() {
  const [puzzle, setPuzzle] = useState<PuzzleResponse | null>(null)
  const [error, setError] = useState<string | null>(null)

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

  return (
    <main id="center">
      <h1>Number Forge</h1>

      {/* Render one of three states: failure, loading, or the fetched puzzle. */}
      {error ? (
        <p role="alert">{error}</p>
      ) : puzzle === null ? (
        <p role="status">Loading puzzle…</p>
      ) : (
        <>
          <p>Starting number: {puzzle.startNumber}</p>
          <p>Target: {puzzle.targetNumber}</p>
          <p>Move limit: {puzzle.maxMoves}</p>

          <section aria-labelledby="operations-heading">
            <h2 id="operations-heading">Available operations</h2>
            <ul>
              {/* Card IDs give React a stable key for each item in the list. */}
              {puzzle.operations.map((operation) => (
                <li key={operation.id}>{operation.description}</li>
              ))}
            </ul>
          </section>
        </>
      )}
    </main>
  )
}

export default App
