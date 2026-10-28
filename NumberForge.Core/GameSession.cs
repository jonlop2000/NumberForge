namespace NumberForge.Core;

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

    public bool TryApplyOperation(IMathOperation operation)
    {
        if (MovesUsed >= MaxMoves || Puzzle.IsTargetReached(CurrentNumber))
        {
            return false;
        }
        CurrentNumber = operation.Apply(CurrentNumber);
        MovesUsed++;
        return true;
    }
}
