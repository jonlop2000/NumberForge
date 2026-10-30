namespace NumberForge.Core;

public class GameSession
{
    private readonly List<IMathOperation> _availableOperations;

    // Each session owns its history. readonly prevents replacing the list; we can still add moves.
    private readonly List<MoveRecord> _history = new List<MoveRecord>();

    public GameSession(Puzzle puzzle, int maxMoves, IEnumerable<IMathOperation> operations)
    {
        Puzzle = puzzle;
        MaxMoves = maxMoves;
        CurrentNumber = puzzle.StartNumber;

        _availableOperations = new List<IMathOperation>(operations);

        // Keep the original hand separate; using a card only changes the available hand.
        StartingOperations = new List<IMathOperation>(_availableOperations).AsReadOnly();
        AvailableOperations = _availableOperations.AsReadOnly();

        // Give callers a view that updates with the list but rejects changes from outside this class.
        History = _history.AsReadOnly();
    }

    public int CurrentNumber { get; private set;}
    public int MovesUsed { get; private set;}
    public Puzzle Puzzle { get; }
    public int MaxMoves { get; }

    // This snapshot preserves the original card order after cards have been used.
    public IReadOnlyList<IMathOperation> StartingOperations { get; }
    public IReadOnlyList<IMathOperation> AvailableOperations { get; }

    // Entries stay in the order the accepted moves happened.
    public IReadOnlyList<MoveRecord> History { get; }

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

        // Save the previous value before replacing it, so history describes both sides of the move.
        int beforeNumber = CurrentNumber;
        CurrentNumber = operation.Apply(CurrentNumber);

        // Rejected moves returned above. Record a move only after its calculation succeeds.
        _history.Add(new MoveRecord(beforeNumber, operation.Description, CurrentNumber));
        MovesUsed++;
        _availableOperations.Remove(operation);
        return true;
    }
}
