using NumberForge.Core;
using System.Threading;

namespace NumberForge.Api.Services;

// Registered once in DI. Both controllers read these same immutable challenge definitions.
public class PuzzleCatalog
{
    public const string DefaultPuzzleId = "demo";

    // Analyze each puzzle when this singleton catalog is created. Requests reuse the saved results.
    private readonly IReadOnlyList<PuzzleDefinition> _featured = new List<PuzzleDefinition>
    {
        CreatePuzzle(DefaultPuzzleId, "First Forge", new Puzzle(3, 14), maxMoves: 2,
            operations: new IMathOperation[]
            {
                new MathOperation(2),
                new MultiplyOperation(2),
                new MathOperation(4)
            }, charm: CharmType.Echo),

        // A successful route must go above 18 before returning to the target.
        CreatePuzzle("detour", "The Detour", new Puzzle(4, 18), maxMoves: 3,
            operations: new IMathOperation[]
            {
                new MathOperation(5),
                new MultiplyOperation(3),
                new MathOperation(-9),
                new MathOperation(4)
            }),

        // Both three-card and four-card solutions exist, giving players a route to improve.
        CreatePuzzle("shortcut", "Find the Shortcut", new Puzzle(2, 24), maxMoves: 4,
            operations: new IMathOperation[]
            {
                new MathOperation(2),
                new MathOperation(4),
                new MultiplyOperation(2),
                new MultiplyOperation(3),
                new MathOperation(8)
            }),

        // Generate one challenge when the shared catalog is created. A fixed seed keeps its rules and
        // identity stable for previews, replays, and saved discoveries when the API starts again.
        new PuzzleGenerator().Generate(seed: 42)
    }.AsReadOnly();

    private readonly DailyPuzzleService _daily;

    public PuzzleCatalog() : this(new DailyPuzzleService(TimeProvider.System)) { }
    public PuzzleCatalog(DailyPuzzleService daily) { _daily = daily; }

    // Four practice puzzles plus today's three challenges: this list never grows with button clicks.
    public IReadOnlyList<PuzzleDefinition> All => _featured.Concat(_daily.GetToday().Puzzles).ToArray();
    public IReadOnlyList<PuzzleDefinition> Practice => _featured;

    public PuzzleDefinition? Get(string id) => _featured.FirstOrDefault(p => p.Id == id) ?? _daily.GetCurrentById(id);

    private static PuzzleDefinition CreatePuzzle(string id, string name, Puzzle puzzle, int maxMoves,
        IEnumerable<IMathOperation> operations, CharmType charm = CharmType.None)
    {
        // Copy the supplied sequence once so analysis and gameplay receive the exact same cards.
        var cards = operations.ToArray();
        var analysis = new PuzzleSolver().Analyze(puzzle, maxMoves, cards, charm);

        // A null minimum means there is no winning route. Catch that setup error before offering the puzzle.
        if (analysis.MinimumMoves is not int minimumMoves)
        {
            throw new InvalidOperationException($"Catalog puzzle '{id}' has no solution within {maxMoves} moves.");
        }

        // Store the calculated metadata in the immutable definition for scoring and route collection.
        return new PuzzleDefinition(id, name, puzzle, maxMoves, minimumMoves, analysis.TotalRoutes, cards, charm);
    }
}
