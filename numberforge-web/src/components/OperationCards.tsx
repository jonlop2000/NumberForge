import type { OperationResponse } from '../types/puzzle'

type OperationCardsProps = {
  operations: readonly OperationResponse[]
  disabled: boolean
  isBusy: boolean
  onSelect?: (operationId: number) => void
}

function OperationCards({ operations, disabled, isBusy, onSelect }: OperationCardsProps) {
  return (
    <section aria-labelledby="operations-heading">
      <h2 id="operations-heading">Available operations</h2>
      <ul className="operation-list" aria-busy={isBusy}>
        {/* Show each card's action so the player can work out its result. */}
        {operations.map((operation) => (
          <li key={operation.id}>
            {onSelect ? (
              <button
                type="button"
                className="operation-card"
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
