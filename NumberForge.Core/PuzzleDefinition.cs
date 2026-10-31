namespace NumberForge.Core;

// A reusable challenge setup. GameSession holds the changing state of one attempt.
public class PuzzleDefinition
{
    public PuzzleDefinition(string id, string name, Puzzle puzzle, int maxMoves, int minimumMoves, int totalRoutes,
        IEnumerable<IMathOperation> operations, CharmType charm = CharmType.None)
    {
        Id = id;
        Name = name;
        Puzzle = puzzle;
        MaxMoves = maxMoves;
        MinimumMoves = minimumMoves;
        TotalRoutes = totalRoutes;
        Charm = charm;

        // Copy the hand so edits to the caller's list cannot change future games.
        Operations = new List<IMathOperation>(operations).AsReadOnly();
    }

    public string Id { get; }
    public string Name { get; }
    public Puzzle Puzzle { get; }
    public int MaxMoves { get; }

    // The verified shortest card count is scoring metadata, kept out of player-facing contracts.
    public int MinimumMoves { get; }

    // Counts all legal winning card sequences within the move budget, including longer wins.
    public int TotalRoutes { get; }
    public IReadOnlyList<IMathOperation> Operations { get; }
    public CharmType Charm { get; }
}
