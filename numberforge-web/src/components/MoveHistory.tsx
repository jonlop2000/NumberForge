import type { MoveResponse } from '../types/puzzle'

type MoveHistoryProps = {
  sessionId: string
  history: readonly MoveResponse[]
}

function MoveHistory({ sessionId, history }: MoveHistoryProps) {
  if (history.length === 0) {
    return null
  }

  return (
    <section aria-labelledby="journey-heading">
      <h2 id="journey-heading">Your journey</h2>
      <ol>
        {history.map((move, index) => (
          <li key={`${sessionId}-${index}`}>
            {move.beforeNumber} → {move.operationDescription} → {move.afterNumber}
          </li>
        ))}
      </ol>
    </section>
  )
}

export default MoveHistory
