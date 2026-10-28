public class GameSession
{
    public GameSession(Puzzle puzzle, int maxMoves)
    {
        Puzzle = puzzle;
        MaxMoves = maxMoves;
        CurrentNumber = puzzle.StartNumber;
    }

    public int CurrentNumber { get; private set;}
    public int MovesUsed { get; private set;}
    public Puzzle Puzzle { get; }
    public int MaxMoves { get; }

    // Accept the shared contract so callers can supply addition or multiplication.
    public bool TryApplyOperation(IMathOperation operation)
    {
        // A finished session rejects moves before changing either state property.
        if (MovesUsed >= MaxMoves || Puzzle.IsTargetReached(CurrentNumber))
        {
            return false;
        }
        // Polymorphism: the supplied object's implementation decides the calculation.
        CurrentNumber = operation.Apply(CurrentNumber);
        MovesUsed++;
        return true;
    }
}
