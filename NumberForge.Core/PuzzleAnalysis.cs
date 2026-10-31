namespace NumberForge.Core;

// An immutable summary of all winning routes for one puzzle setup.
public sealed class PuzzleAnalysis
{
    public PuzzleAnalysis(int? minimumMoves, int totalRoutes)
    {
        MinimumMoves = minimumMoves;
        TotalRoutes = totalRoutes;
    }

    // null means no solution exists; zero means the starting number already meets the target.
    public int? MinimumMoves { get; }

    // Different ordered card IDs count separately, even if they visit the same numbers.
    public int TotalRoutes { get; }
}
