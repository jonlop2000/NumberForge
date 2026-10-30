import type { OperationResponse } from '../types/puzzle'

type OperationCardsProps = {
  operations: readonly OperationResponse[]
  disabled: boolean
  isBusy: boolean
  // App supplies this callback during a game. Without it, the cards are preview text.
  onSelect?: (operationId: number) => void
}

function OperationCards({ operations, disabled, isBusy, onSelect }: OperationCardsProps) {
  return (
    <section aria-labelledby="operations-heading">
      <h2 id="operations-heading">Available operations</h2>
      <ul className="operation-list" aria-busy={isBusy}>
        {operations.map((operation) => (
          <li key={operation.id}>
            {onSelect ? (
              <button
                type="button"
                className="operation-card"
                // Report the stable card ID to App when clicked; App performs the API request.
                onClick={() => onSelect(operation.id)}
                disabled={disabled}
              >
                {operation.description}
              </button>
            ) : operation.description}
          </li>
        ))}
      </ul>
      {onSelect !== undefined && operations.length === 0 && <p>No cards remaining.</p>}
    </section>
  )
}

export default OperationCards
