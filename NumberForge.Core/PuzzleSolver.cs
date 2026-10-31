namespace NumberForge.Core;

// Exhaustively analyzes small hands. Every branch keeps its own number and Echo state.
public class PuzzleSolver
{
    // Eight cards have at most 109,601 ordered prefixes; this also bounds recursion depth and memory.
    public const int MaximumHandSize = 8;

    public PuzzleAnalysis Analyze(Puzzle puzzle, int maxMoves, IEnumerable<IMathOperation> operations,
        CharmType charm = CharmType.None, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(puzzle);
        ArgumentNullException.ThrowIfNull(operations);
        ArgumentOutOfRangeException.ThrowIfNegative(maxMoves);
        GameRules.ValidateCharm(charm);
        cancellationToken.ThrowIfCancellationRequested();

        // Stop reading once an oversized hand is detected, even if the supplied sequence is unbounded.
        var cards = operations.Take(MaximumHandSize + 1).ToArray();
        if (cards.Length > MaximumHandSize)
        {
            throw new ArgumentOutOfRangeException(nameof(operations), $"The solver supports at most {MaximumHandSize} cards.");
        }
        GameRules.ValidateHand(cards);

        var used = new bool[cards.Length];
        int? minimumMoves = null;
        int totalRoutes = 0;

        Explore(puzzle.StartNumber, movesUsed: 0, echoUsed: false);
        return new PuzzleAnalysis(minimumMoves, totalRoutes);

        void Explore(int currentNumber, int movesUsed, bool echoUsed)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (puzzle.IsTargetReached(currentNumber))
            {
                // Depth-first search can find a longer win first, so update the minimum for every win.
                minimumMoves = Math.Min(minimumMoves ?? movesUsed, movesUsed);
                totalRoutes = checked(totalRoutes + 1);
                return; // A winning route cannot be extended in a real game.
            }
            if (GameRules.IsFinished(puzzle, currentNumber, movesUsed, maxMoves, cards.Length - movesUsed))
            {
                return;
            }

            for (int index = 0; index < cards.Length; index++)
            {
                if (used[index]
                    || !GameRules.TryCalculate(currentNumber, cards[index], charm, echoUsed,
                        out int afterNumber, out bool usesEcho))
                {
                    continue;
                }

                // Explore this card, then make it available to the next branch. Equal numbers are never
                // merged: different ordered card IDs still represent different collectible routes.
                used[index] = true;
                Explore(afterNumber, movesUsed + 1, echoUsed || usesEcho);
                used[index] = false;
            }
        }
    }
}
