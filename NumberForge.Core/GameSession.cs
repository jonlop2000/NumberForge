namespace NumberForge.Core;

public class GameSession
{
    private readonly List<IMathOperation> _availableOperations;
    private readonly List<IMathOperation> _startingOperations;

    private readonly List<MoveRecord> _history = new List<MoveRecord>();

    // Reuse a catalog definition while the existing constructor gives this attempt its own hand.
    public GameSession(PuzzleDefinition definition)
        : this((definition ?? throw new ArgumentNullException(nameof(definition))).Puzzle,
            definition.MaxMoves, definition.Operations, definition.Charm)
    {
        Definition = definition;
    }

    public GameSession(Puzzle puzzle, int maxMoves, IEnumerable<IMathOperation> operations,
        CharmType charm = CharmType.None)
    {
        ArgumentNullException.ThrowIfNull(puzzle);
        ArgumentNullException.ThrowIfNull(operations);
        ArgumentOutOfRangeException.ThrowIfNegative(maxMoves);
        GameRules.ValidateCharm(charm);

        Puzzle = puzzle;
        MaxMoves = maxMoves;
        CurrentNumber = puzzle.StartNumber;

        Charm = charm;

        _availableOperations = new List<IMathOperation>(operations);
        GameRules.ValidateHand(_availableOperations);

        _startingOperations = new List<IMathOperation>(_availableOperations);
        StartingOperations = _startingOperations.AsReadOnly();
        AvailableOperations = _availableOperations.AsReadOnly();

        History = _history.AsReadOnly();
    }

    public int CurrentNumber { get; private set;}
    public int MovesUsed { get; private set;}
    public Puzzle Puzzle { get; }
    public int MaxMoves { get; }

    // Catalog games retain their identity for page refreshes and replay.
    // Standalone games, such as the console's custom puzzle, can omit a definition.
    public PuzzleDefinition? Definition { get; }

    public CharmType Charm { get; }

    public bool EchoUsed { get; private set; }

    public IReadOnlyList<IMathOperation> StartingOperations { get; }
    public IReadOnlyList<IMathOperation> AvailableOperations { get; }

    public IReadOnlyList<MoveRecord> History { get; }

    // All interfaces use these same rules to decide when an attempt has ended.
    public bool IsFinished => GameRules.IsFinished(Puzzle, CurrentNumber, MovesUsed, MaxMoves, _availableOperations.Count);

    // Only a win with a known minimum can be graded. Active games and losses have no grade.
    public bool? IsPerfect => Definition is not null && Puzzle.IsTargetReached(CurrentNumber)
        ? MovesUsed == Definition.MinimumMoves
        : null;

    // int? lets us return null when this card cannot currently be played.
    public int? PreviewOperation(IMathOperation operation)
    {
        if (!CanApplyOperation(operation))
        {
            return null;
        }

        // Reading the calculated result leaves the number, cards, history, and charm untouched.
        return GameRules.TryCalculate(CurrentNumber, operation, Charm, EchoUsed, out int result, out _)
            ? result : null;
    }

    public bool TryApplyOperation(IMathOperation operation)
    {
        if (!CanApplyOperation(operation))
        {
            return false;
        }

        int beforeNumber = CurrentNumber;
        string operationDescription = operation.Description;
        if (!GameRules.TryCalculate(CurrentNumber, operation, Charm, EchoUsed, out int afterNumber, out bool usesEcho))
        {
            return false;
        }

        // A real move commits the calculation and spends Echo if this card activated it.
        CurrentNumber = afterNumber;
        if (usesEcho)
        {
            EchoUsed = true;
            operationDescription += " (Echo ×2)";
        }

        // Record the original card ID, not its current position in the shrinking hand.
        int operationId = _startingOperations.FindIndex(card => ReferenceEquals(card, operation)) + 1;
        _history.Add(new MoveRecord(operationId, beforeNumber, operationDescription, CurrentNumber));
        MovesUsed++;
        _availableOperations.RemoveAt(_availableOperations.FindIndex(card => ReferenceEquals(card, operation)));
        return true;
    }

    private bool CanApplyOperation(IMathOperation operation)
    {
        // Previewing and playing agree on which cards are still legal to use.
        return _availableOperations.Any(card => ReferenceEquals(card, operation))
            && !IsFinished;
    }
}
