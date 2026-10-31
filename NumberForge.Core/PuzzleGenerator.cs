using System.Globalization;

namespace NumberForge.Core;

// Build a legal route, then let the solver check whether the resulting puzzle fits the requested profile.
public class PuzzleGenerator
{
    private const int MaxAttempts = 250;

    // Preserve the original practice recipe and its seeded IDs for existing catalog callers.
    private static readonly PuzzleGenerationProfile PracticeProfile = new PuzzleGenerationProfile(
        maxMoves: 4, minimumSolutionMoves: 3, maximumSolutionMoves: 3,
        minimumRoutes: 2, maximumRoutes: int.MaxValue);

    private readonly PuzzleSolver _solver = new PuzzleSolver();

    public PuzzleDefinition Generate(int seed, CharmType charm = CharmType.None,
        CancellationToken cancellationToken = default)
    {
        string id = $"generated-v1-{seed.ToString(CultureInfo.InvariantCulture)}-{charm.ToString().ToLowerInvariant()}";
        return GeneratePuzzle(seed, PracticeProfile, charm, id, cancellationToken);
    }

    public PuzzleDefinition Generate(int seed, PuzzleGenerationProfile profile, CharmType charm = CharmType.None,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(profile);

        // Include every profile setting in the identity. Tuning a profile must not reuse another puzzle's ID.
        string id = FormattableString.Invariant(
            $"generated-v2-{profile.MaxMoves}-{profile.MinimumSolutionMoves}-{profile.MaximumSolutionMoves}-{profile.MinimumRoutes}-{profile.MaximumRoutes}-{seed}-{charm.ToString().ToLowerInvariant()}");
        return GeneratePuzzle(seed, profile, charm, id, cancellationToken);
    }

    private PuzzleDefinition GeneratePuzzle(int seed, PuzzleGenerationProfile profile, CharmType charm, string id,
        CancellationToken cancellationToken)
    {
        if (charm != CharmType.None && charm != CharmType.Echo)
        {
            throw new ArgumentOutOfRangeException(nameof(charm), charm, "This generator supports None and Echo.");
        }

        // A fresh seeded random source makes each call repeatable, regardless of earlier calls.
        var random = new Random(seed);

        // Stop after a fixed amount of work instead of searching forever for a suitable puzzle.
        for (int attempt = 0; attempt < MaxAttempts; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int startNumber = random.Next(1, 10);
            var cards = CreateHand(random, profile.HandSize);

            // Shuffle the hidden route separately from the displayed hand so card order gives no solution away.
            var route = Enumerable.Range(0, cards.Length).ToArray();
            random.Shuffle(route);

            // We do not know the real target yet. This temporary target cannot be reached with our small
            // cards in at most five moves, allowing GameSession to resolve every play, including Echo, normally.
            var construction = new GameSession(new Puzzle(startNumber, int.MaxValue), profile.MaxMoves, cards, charm);
            foreach (int cardIndex in route.Take(profile.MaxMoves))
            {
                if (!construction.TryApplyOperation(cards[cardIndex]))
                {
                    throw new InvalidOperationException("The generated construction route contains an illegal move.");
                }
            }

            int targetNumber = construction.CurrentNumber;

            // Keep targets approachable. Also reject routes that reached their final target earlier:
            // a real game would already have ended, so those would not be valid full-length solutions.
            if (targetNumber < 10 || targetNumber > 100
                || construction.History.Take(profile.MaxMoves - 1).Any(move => move.AfterNumber == targetNumber))
            {
                continue;
            }

            var puzzle = new Puzzle(startNumber, targetNumber);
            var analysis = _solver.Analyze(puzzle, profile.MaxMoves, cards, charm, cancellationToken);

            // A candidate needs a shortcut within the profile's range. The construction route already
            // proves that a longer win exists, using the full move budget.
            if (analysis.MinimumMoves is not int minimumMoves
                || minimumMoves < profile.MinimumSolutionMoves || minimumMoves > profile.MaximumSolutionMoves)
            {
                continue;
            }

            // Count all legal winning card orders, including longer routes, before accepting the route cap.
            if (analysis.TotalRoutes < profile.MinimumRoutes || analysis.TotalRoutes > profile.MaximumRoutes)
            {
                continue;
            }

            return new PuzzleDefinition(id, "Generated Forge", puzzle, profile.MaxMoves, minimumMoves,
                analysis.TotalRoutes, cards, charm);
        }

        throw new InvalidOperationException(
            $"Could not generate puzzle '{id}' within {MaxAttempts} attempts. Try a different seed.");
    }

    private static IMathOperation[] CreateHand(Random random, int handSize)
    {
        // Distinct positive additions avoid filling the hand with identical cards. A negative addition
        // allows detours, and multiplication makes card order matter. There are no +0 or ×1 cards.
        var positiveAmounts = Enumerable.Range(2, 8).ToArray();
        random.Shuffle(positiveAmounts);
        var cards = new List<IMathOperation>();

        // Reserve three slots for the negative addition and the two multiplication cards.
        // The three daily profiles therefore get one, two, or three distinct positive additions.
        for (int index = 0; index < handSize - 3; index++)
        {
            cards.Add(new MathOperation(positiveAmounts[index]));
        }
        cards.Add(new MathOperation(-random.Next(2, 10)));
        cards.Add(new MultiplyOperation(2));
        cards.Add(new MultiplyOperation(3));

        var hand = cards.ToArray();
        random.Shuffle(hand);
        return hand;
    }
}
