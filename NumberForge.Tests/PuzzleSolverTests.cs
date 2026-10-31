using NumberForge.Api.Services;
using NumberForge.Core;

namespace NumberForge.Tests;

public class PuzzleSolverTests
{
    [Fact]
    public void Analyze_OverflowCannotCreateAWrappedWinningRoute()
    {
        var result = new PuzzleSolver().Analyze(new Puzzle(int.MaxValue, -2), 1,
            new IMathOperation[] { new MultiplyOperation(2) });

        Assert.Null(result.MinimumMoves);
        Assert.Equal(0, result.TotalRoutes);
    }

    [Fact]
    public void Analyze_EchoOverflowKeepsOtherBranchesAvailable()
    {
        var result = new PuzzleSolver().Analyze(new Puzzle(int.MaxValue - 1, int.MaxValue), 2,
            new IMathOperation[] { new MathOperation(1), new MathOperation(-1) }, CharmType.Echo);

        Assert.Equal(2, result.MinimumMoves);
        Assert.Equal(1, result.TotalRoutes);
    }

    [Fact]
    public void Analyze_StopsReadingAnUnboundedHandAtTheSupportedLimit()
    {
        int enumerated = 0;
        IEnumerable<IMathOperation> InfiniteCards()
        {
            while (true)
            {
                enumerated++;
                yield return new MathOperation(1);
            }
        }

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new PuzzleSolver().Analyze(new Puzzle(0, 100), 5, InfiniteCards()));
        Assert.Equal(PuzzleSolver.MaximumHandSize + 1, enumerated);
    }

    [Fact]
    public void Analyze_LargeMoveBudgetStillStopsWhenTheSmallHandIsExhausted()
    {
        var cards = Enumerable.Range(0, PuzzleSolver.MaximumHandSize).Select(_ => new MathOperation(1));

        var result = new PuzzleSolver().Analyze(new Puzzle(0, 100), int.MaxValue, cards);

        Assert.Null(result.MinimumMoves);
        Assert.Equal(0, result.TotalRoutes);
    }

    [Fact]
    public void Analyze_CancellationStopsAnInProgressSearch()
    {
        using var cancellation = new CancellationTokenSource();
        var cards = new IMathOperation[] { new CancellingOperation(cancellation), new MathOperation(2) };

        Assert.Throws<OperationCanceledException>(() => new PuzzleSolver().Analyze(
            new Puzzle(0, 100), 2, cards, cancellationToken: cancellation.Token));
    }

    [Fact]
    public void Analyze_RejectsUnknownCharmsNullCardsAndRepeatedCardInstances()
    {
        var card = new MathOperation(2);
        var puzzle = new Puzzle(0, 10);
        var solver = new PuzzleSolver();
        Assert.Throws<ArgumentOutOfRangeException>(() => solver.Analyze(puzzle, 2, new[] { card }, (CharmType)99));
        Assert.Throws<ArgumentException>(() => solver.Analyze(puzzle, 2, new IMathOperation[] { null! }));
        Assert.Throws<ArgumentException>(() => solver.Analyze(puzzle, 2, new IMathOperation[] { card, card }));
    }

    [Theory]
    [InlineData("demo", 2, 2)]
    [InlineData("detour", 3, 1)]
    [InlineData("shortcut", 3, 19)]
    public void Analyze_CatalogPuzzle_FindsVerifiedMinimumAndRouteCount(string puzzleId,
        int minimumMoves, int totalRoutes)
    {
        var definition = new PuzzleCatalog().Get(puzzleId)!;

        // Supply only the rules. Expected answers stay explicit so this test can catch rule changes.
        var result = new PuzzleSolver().Analyze(definition.Puzzle, definition.MaxMoves,
            definition.Operations, definition.Charm);

        Assert.Equal(minimumMoves, result.MinimumMoves);
        Assert.Equal(totalRoutes, result.TotalRoutes);
    }

    [Fact]
    public void Analyze_UnreachableTarget_ReturnsNoMinimumAndZeroRoutes()
    {
        var result = new PuzzleSolver().Analyze(new Puzzle(1, 42), 3,
            new IMathOperation[] { new MathOperation(2), new MultiplyOperation(2) });

        Assert.Null(result.MinimumMoves);
        Assert.Equal(0, result.TotalRoutes);
    }

    [Fact]
    public void Analyze_CannotReuseACardEvenWhenMovesRemain()
    {
        // Reusing +2 would reach 4, but there is only one copy in this hand.
        var result = new PuzzleSolver().Analyze(new Puzzle(0, 4), 3,
            new IMathOperation[] { new MathOperation(2) });

        Assert.Null(result.MinimumMoves);
        Assert.Equal(0, result.TotalRoutes);
    }

    [Theory]
    [InlineData(0, null, 0)]
    [InlineData(1, null, 0)]
    [InlineData(2, 2, 1)]
    public void Analyze_OnlyCountsWinsWithinTheMoveBudget(int maxMoves, int? minimumMoves, int totalRoutes)
    {
        // The only solution is 1 -> Add 2 -> 3 -> Multiply by 2 -> 6.
        var result = new PuzzleSolver().Analyze(new Puzzle(1, 6), maxMoves,
            new IMathOperation[] { new MathOperation(2), new MultiplyOperation(2) });

        Assert.Equal(minimumMoves, result.MinimumMoves);
        Assert.Equal(totalRoutes, result.TotalRoutes);
    }

    [Fact]
    public void Analyze_EmptyHandWithUnmetTarget_HasNoSolution()
    {
        var result = new PuzzleSolver().Analyze(new Puzzle(3, 5), 2, Array.Empty<IMathOperation>());

        Assert.Null(result.MinimumMoves);
        Assert.Equal(0, result.TotalRoutes);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(3)]
    public void Analyze_StartingAtTheTarget_CountsOnlyTheEmptyRoute(int maxMoves)
    {
        var result = new PuzzleSolver().Analyze(new Puzzle(3, 3), maxMoves,
            new IMathOperation[] { new MathOperation(0), new MathOperation(1) });

        Assert.Equal(0, result.MinimumMoves);
        Assert.Equal(1, result.TotalRoutes);
    }

    [Fact]
    public void Analyze_DoesNotExtendWinningRoutes()
    {
        // [+2] and [+0, +2] win. [+2, +0] is illegal because the game already ended.
        var result = new PuzzleSolver().Analyze(new Puzzle(0, 2), 2,
            new IMathOperation[] { new MathOperation(2), new MathOperation(0) });

        Assert.Equal(1, result.MinimumMoves);
        Assert.Equal(2, result.TotalRoutes);
    }

    [Fact]
    public void Analyze_IdenticalCards_CountsEachCardOrderSeparately()
    {
        // These are two distinct cards. Both orders visit 0 -> 2 -> 4 but count as different routes.
        var result = new PuzzleSolver().Analyze(new Puzzle(0, 4), 2,
            new IMathOperation[] { new MathOperation(2), new MathOperation(2) });

        Assert.Equal(2, result.MinimumMoves);
        Assert.Equal(2, result.TotalRoutes);
    }

    [Fact]
    public void Analyze_Echo_RepeatsOnlyTheFirstPositiveAdditionInEachRoute()
    {
        // [+4] wins immediately through Echo. [+2, +4] wins with only the +2 doubled.
        var result = new PuzzleSolver().Analyze(new Puzzle(3, 11), 2,
            new IMathOperation[] { new MathOperation(2), new MathOperation(4) }, CharmType.Echo);

        Assert.Equal(1, result.MinimumMoves);
        Assert.Equal(2, result.TotalRoutes);
    }

    [Theory]
    [InlineData(5, null, 0)]
    [InlineData(7, 1, 1)]
    public void Analyze_Echo_ResolvesBothActivationsAsOneMoveBeforeCheckingTheTarget(int target,
        int? minimumMoves, int totalRoutes)
    {
        // Echo passes through 5 but finishes at 7. Only 7 is a winning target for this card.
        var result = new PuzzleSolver().Analyze(new Puzzle(3, target), 1,
            new IMathOperation[] { new MathOperation(2) }, CharmType.Echo);

        Assert.Equal(minimumMoves, result.MinimumMoves);
        Assert.Equal(totalRoutes, result.TotalRoutes);
    }

    [Fact]
    public void Analyze_NegativeAddition_DoesNotConsumeEcho()
    {
        // Both [-2, +3] and [+3, -2] reach 7 because only the positive addition repeats.
        var result = new PuzzleSolver().Analyze(new Puzzle(3, 7), 2,
            new IMathOperation[] { new MathOperation(-2), new MathOperation(3) }, CharmType.Echo);

        Assert.Equal(2, result.MinimumMoves);
        Assert.Equal(2, result.TotalRoutes);
    }

    private sealed class CancellingOperation(CancellationTokenSource cancellation) : IMathOperation
    {
        public string Description => "Cancel the test search";

        public int Apply(int input)
        {
            cancellation.Cancel();
            return input + 1;
        }
    }
}
