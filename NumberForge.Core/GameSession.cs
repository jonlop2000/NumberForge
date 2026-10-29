namespace NumberForge.Core;

public class GameSession
{
    private readonly List<IMathOperation> _availableOperations;

    public GameSession(Puzzle puzzle, int maxMoves, IEnumerable<IMathOperation> operations)
    {
        Puzzle = puzzle;
        MaxMoves = maxMoves;
        CurrentNumber = puzzle.StartNumber;

        _availableOperations = new List<IMathOperation>(operations);

        // Keep the original hand separate; using a card only changes the available hand.
        StartingOperations = new List<IMathOperation>(_availableOperations).AsReadOnly();
        AvailableOperations = _availableOperations.AsReadOnly();
    }

    public int CurrentNumber { get; private set;}
    public int MovesUsed { get; private set;}
    public Puzzle Puzzle { get; }
    public int MaxMoves { get; }

    // This snapshot preserves the original card order after cards have been used.
    public IReadOnlyList<IMathOperation> StartingOperations { get; }
    public IReadOnlyList<IMathOperation> AvailableOperations { get; }

    public bool TryApplyOperation(IMathOperation operation)
    {
        if (!_availableOperations.Contains(operation))
        {
            return false;
        }

        if (MovesUsed >= MaxMoves || Puzzle.IsTargetReached(CurrentNumber))
        {
            return false;
        }
        CurrentNumber = operation.Apply(CurrentNumber);
        MovesUsed++;
        _availableOperations.Remove(operation);
        return true;
    }
}
