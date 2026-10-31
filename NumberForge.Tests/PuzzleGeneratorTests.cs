using NumberForge.Core;

namespace NumberForge.Tests;

public class PuzzleGeneratorTests
{
    [Fact]
    public void Generate_CancelledRequest_DoesNotContinueSearching()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var generator = new PuzzleGenerator();

        Assert.Throws<OperationCanceledException>(() => generator.Generate(42, cancellationToken: cancellation.Token));
        Assert.Throws<OperationCanceledException>(() => generator.Generate(42, PuzzleGenerationProfile.FiveMoves,
            cancellationToken: cancellation.Token));
    }

    [Theory]
    [InlineData(3, CharmType.None)]
    [InlineData(3, CharmType.Echo)]
    [InlineData(4, CharmType.None)]
    [InlineData(4, CharmType.Echo)]
    [InlineData(5, CharmType.None)]
    [InlineData(5, CharmType.Echo)]
    public void Generate_ProfileSamples_RespectSolutionBoundsRouteCapsAndTheFullMoveBudget(int maxMoves, CharmType charm)
    {
        var profile = ProfileFor(maxMoves);
        var generator = new PuzzleGenerator();
        var seeds = Enumerable.Range(0, 24).Concat(new[] { -1, -17, int.MinValue, int.MaxValue });

        foreach (int seed in seeds)
        {
            var definition = generator.Generate(seed, profile, charm);

            Assert.Equal(maxMoves, definition.MaxMoves);
            Assert.Equal(maxMoves + 1, definition.Operations.Count);
            Assert.Equal(definition.Operations.Count, definition.Operations.Select(card => card.Description).Distinct().Count());
            Assert.Equal(charm, definition.Charm);
            Assert.InRange(definition.Puzzle.StartNumber, 1, 9);
            Assert.InRange(definition.Puzzle.TargetNumber, 10, 100);

            // Independently play every legal card order, including routes longer than the shortest solution.
            var wins = FindWinningRoutes(definition, Array.Empty<int>()).ToArray();
            Assert.NotEmpty(wins);
            Assert.InRange(wins.Min(game => game.MovesUsed), profile.MinimumSolutionMoves, profile.MaximumSolutionMoves);
            Assert.InRange(wins.Length, profile.MinimumRoutes, profile.MaximumRoutes);
            Assert.Contains(wins, game => game.MovesUsed == maxMoves);
            Assert.Equal(wins.Min(game => game.MovesUsed), definition.MinimumMoves);
            Assert.Equal(wins.Length, definition.TotalRoutes);
        }
    }

    [Theory]
    [InlineData(3, CharmType.None)]
    [InlineData(3, CharmType.Echo)]
    [InlineData(4, CharmType.None)]
    [InlineData(4, CharmType.Echo)]
    [InlineData(5, CharmType.None)]
    [InlineData(5, CharmType.Echo)]
    public void Generate_SameSeedProfileAndCharm_ReproducesThePuzzle(int maxMoves, CharmType charm)
    {
        var profile = ProfileFor(maxMoves);
        var generator = new PuzzleGenerator();
        var first = generator.Generate(42, profile, charm);
        generator.Generate(99, profile, charm);
        var repeated = generator.Generate(42, profile, charm);

        // Equivalent profile objects must work too; identity depends on settings, not object references.
        var copiedProfile = new PuzzleGenerationProfile(profile.MaxMoves, profile.MinimumSolutionMoves,
            profile.MaximumSolutionMoves, profile.MinimumRoutes, profile.MaximumRoutes);
        var separate = new PuzzleGenerator().Generate(42, copiedProfile, charm);

        Assert.Equal(Fingerprint(first), Fingerprint(repeated));
        Assert.Equal(Fingerprint(first), Fingerprint(separate));
        Assert.Equal(first.Id, repeated.Id);
        Assert.Equal(first.Id, separate.Id);
        Assert.Equal(first.MinimumMoves, separate.MinimumMoves);
        Assert.Equal(first.TotalRoutes, separate.TotalRoutes);
    }

    [Fact]
    public void Generate_ChangedProfileSettings_GetDifferentPuzzleIdentities()
    {
        var generator = new PuzzleGenerator();
        var original = generator.Generate(42, PuzzleGenerationProfile.FourMoves);
        var changedCap = generator.Generate(42, new PuzzleGenerationProfile(
            maxMoves: 4, minimumSolutionMoves: 2, maximumSolutionMoves: 3, minimumRoutes: 2, maximumRoutes: 12));
        var changedBudget = generator.Generate(42, PuzzleGenerationProfile.ThreeMoves);
        var changedCharm = generator.Generate(42, PuzzleGenerationProfile.FourMoves, CharmType.Echo);

        Assert.NotEqual(original.Id, changedCap.Id);
        Assert.NotEqual(original.Id, changedBudget.Id);
        Assert.NotEqual(original.Id, changedCharm.Id);
    }

    [Fact]
    public void Generate_DefaultRecipe_KeepsTheExistingSeed42Puzzle()
    {
        var definition = new PuzzleGenerator().Generate(42);

        Assert.Equal("generated-v1-42-none", definition.Id);
        Assert.Equal(7, definition.Puzzle.StartNumber);
        Assert.Equal(15, definition.Puzzle.TargetNumber);
        Assert.Equal(3, definition.MinimumMoves);
        Assert.Equal(4, definition.TotalRoutes);
        Assert.Equal(new[] { "Multiply by 2", "Add 2", "Add -3", "Multiply by 3", "Add 3" },
            definition.Operations.Select(card => card.Description));
    }

    [Fact]
    public void Generate_NullProfile_IsRejected()
    {
        var exception = Assert.Throws<ArgumentNullException>(() => new PuzzleGenerator().Generate(42, null!));

        Assert.Equal("profile", exception.ParamName);
    }

    [Theory]
    [InlineData(CharmType.None)]
    [InlineData(CharmType.Echo)]
    public void Generate_SampleSeeds_ProducesSolvablePuzzlesWithShorterAndLongerRoutes(CharmType charm)
    {
        var generator = new PuzzleGenerator();
        var seeds = Enumerable.Range(0, 32).Concat(new[] { -1, -17, int.MinValue, int.MaxValue });

        // Fixed seeds cover different hands without making this test depend on chance.
        foreach (int seed in seeds)
        {
            var definition = generator.Generate(seed, charm);

            Assert.InRange(definition.Puzzle.StartNumber, 1, 9);
            Assert.InRange(definition.Puzzle.TargetNumber, 10, 100);
            Assert.Equal(charm, definition.Charm);
            Assert.Equal(5, definition.Operations.Count);
            Assert.Equal(5, definition.Operations.Select(card => card.Description).Distinct().Count());
            Assert.Equal(4, definition.MaxMoves);

            // Independently play all legal routes. This verifies the returned puzzle, not just its metadata.
            var wins = FindWinningRoutes(definition, Array.Empty<int>()).ToArray();
            Assert.NotEmpty(wins);
            Assert.Equal(3, wins.Min(game => game.MovesUsed));
            Assert.Contains(wins, game => game.MovesUsed == 4);
            Assert.Equal(wins.Min(game => game.MovesUsed), definition.MinimumMoves);
            Assert.Equal(wins.Length, definition.TotalRoutes);
        }
    }

    [Theory]
    [InlineData(CharmType.None)]
    [InlineData(CharmType.Echo)]
    public void Generate_SameSeedAndCharm_ReproducesThePuzzleRegardlessOfEarlierCalls(CharmType charm)
    {
        var generator = new PuzzleGenerator();
        var first = generator.Generate(42, charm);
        generator.Generate(99, charm);

        var repeated = generator.Generate(42, charm);
        var anotherGenerator = new PuzzleGenerator().Generate(42, charm);

        Assert.Equal(Fingerprint(first), Fingerprint(repeated));
        Assert.Equal(Fingerprint(first), Fingerprint(anotherGenerator));
        Assert.Equal(first.Id, repeated.Id);
        Assert.Equal(first.Id, anotherGenerator.Id);
        Assert.Equal(first.MinimumMoves, repeated.MinimumMoves);
        Assert.Equal(first.TotalRoutes, repeated.TotalRoutes);
    }

    [Fact]
    public void Generate_DifferentSeeds_ProducesDifferentPuzzleContent()
    {
        var generator = new PuzzleGenerator();
        var fingerprints = Enumerable.Range(0, 10)
            .Select(seed => Fingerprint(generator.Generate(seed)))
            .Distinct()
            .ToArray();

        // Compare actual rules and ordered cards, excluding IDs; a new label alone is not a new puzzle.
        Assert.True(fingerprints.Length > 1);
    }

    [Fact]
    public void Generate_DifferentCharms_HaveSeparatePuzzleIdentities()
    {
        var generator = new PuzzleGenerator();

        var ordinary = generator.Generate(42);
        var echo = generator.Generate(42, CharmType.Echo);

        Assert.NotEqual(ordinary.Id, echo.Id);
    }

    [Fact]
    public void Generate_UnknownCharm_IsRejected()
    {
        var exception = Assert.Throws<ArgumentOutOfRangeException>(
            () => new PuzzleGenerator().Generate(42, (CharmType)999));

        Assert.Equal("charm", exception.ParamName);
    }

    private static PuzzleGenerationProfile ProfileFor(int maxMoves)
    {
        return maxMoves switch
        {
            3 => PuzzleGenerationProfile.ThreeMoves,
            4 => PuzzleGenerationProfile.FourMoves,
            5 => PuzzleGenerationProfile.FiveMoves,
            _ => throw new ArgumentOutOfRangeException(nameof(maxMoves))
        };
    }

    private static string Fingerprint(PuzzleDefinition definition)
    {
        return $"{definition.Puzzle.StartNumber}|{definition.Puzzle.TargetNumber}|{definition.MaxMoves}|"
            + $"{definition.Charm}|{string.Join(",", definition.Operations.Select(card => card.Description))}";
    }

    private static IEnumerable<GameSession> FindWinningRoutes(PuzzleDefinition definition, int[] route)
    {
        var game = new GameSession(definition);
        foreach (int cardIndex in route)
        {
            Assert.True(game.TryApplyOperation(definition.Operations[cardIndex]));
        }

        if (game.Puzzle.IsTargetReached(game.CurrentNumber))
        {
            yield return game;
            yield break;
        }

        if (game.IsFinished)
        {
            yield break;
        }

        // Explore every unused card recursively, stopping each branch when its game ends.
        for (int cardIndex = 0; cardIndex < definition.Operations.Count; cardIndex++)
        {
            if (!route.Contains(cardIndex))
            {
                foreach (var win in FindWinningRoutes(definition, route.Append(cardIndex).ToArray()))
                {
                    yield return win;
                }
            }
        }
    }
}
