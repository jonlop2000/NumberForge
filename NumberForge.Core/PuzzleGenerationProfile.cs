namespace NumberForge.Core;

// A profile describes which generated puzzles we will accept. Its get-only properties keep shared presets safe.
public sealed class PuzzleGenerationProfile
{
    // These are initial daily-level settings. Route caps keep the collection side quest reasonably small.
    public static PuzzleGenerationProfile ThreeMoves { get; } = new PuzzleGenerationProfile(
        maxMoves: 3, minimumSolutionMoves: 2, maximumSolutionMoves: 2, minimumRoutes: 2, maximumRoutes: 4);

    public static PuzzleGenerationProfile FourMoves { get; } = new PuzzleGenerationProfile(
        maxMoves: 4, minimumSolutionMoves: 2, maximumSolutionMoves: 3, minimumRoutes: 2, maximumRoutes: 8);

    public static PuzzleGenerationProfile FiveMoves { get; } = new PuzzleGenerationProfile(
        maxMoves: 5, minimumSolutionMoves: 3, maximumSolutionMoves: 4, minimumRoutes: 2, maximumRoutes: 12);

    public PuzzleGenerationProfile(int maxMoves, int minimumSolutionMoves, int maximumSolutionMoves,
        int minimumRoutes, int maximumRoutes)
    {
        // Keep this exhaustive generator within the small hands it was designed to search.
        if (maxMoves < 3 || maxMoves > 5)
        {
            throw new ArgumentOutOfRangeException(nameof(maxMoves), "Supported move limits are 3, 4, and 5.");
        }
        if (minimumSolutionMoves < 1 || minimumSolutionMoves >= maxMoves)
        {
            throw new ArgumentOutOfRangeException(nameof(minimumSolutionMoves),
                "The shortest solution must use at least one card and leave room for a longer route.");
        }
        if (maximumSolutionMoves < minimumSolutionMoves || maximumSolutionMoves >= maxMoves)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumSolutionMoves),
                "The upper solution bound must be at least the lower bound and below the move limit.");
        }
        if (minimumRoutes < 2)
        {
            throw new ArgumentOutOfRangeException(nameof(minimumRoutes), "Route collection requires at least two solutions.");
        }
        if (maximumRoutes < minimumRoutes)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumRoutes), "The route cap must be at least the minimum route count.");
        }

        MaxMoves = maxMoves;
        MinimumSolutionMoves = minimumSolutionMoves;
        MaximumSolutionMoves = maximumSolutionMoves;
        MinimumRoutes = minimumRoutes;
        MaximumRoutes = maximumRoutes;
    }

    public int MaxMoves { get; }

    // One spare card gives the player a choice of which cards to use, as well as their order.
    public int HandSize => MaxMoves + 1;

    // These bounds describe the verified shortest solution, not the length of every winning route.
    public int MinimumSolutionMoves { get; }
    public int MaximumSolutionMoves { get; }
    public int MinimumRoutes { get; }
    public int MaximumRoutes { get; }
}
