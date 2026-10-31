using NumberForge.Api.Services;
using NumberForge.Core;

namespace NumberForge.Tests;

public class DailyPuzzleServiceTests
{
    [Fact]
    public void Today_IsSharedUntilTheUtcBoundaryAndThenReplaced()
    {
        var clock = new TestClock(new DateTimeOffset(2026, 10, 30, 23, 59, 59, TimeSpan.Zero));
        var service = new DailyPuzzleService(clock);

        var first = service.GetToday();
        Assert.Same(first, service.GetToday());
        Assert.Equal(new DateOnly(2026, 10, 30), first.Date);
        Assert.Equal(new DateTimeOffset(2026, 10, 30, 0, 0, 0, TimeSpan.Zero), first.AvailableFromUtc);
        Assert.Equal(new DateTimeOffset(2026, 10, 31, 0, 0, 0, TimeSpan.Zero), first.ExpiresAtUtc);
        Assert.Equal(new[] { "daily-v1-20261030-1", "daily-v1-20261030-2", "daily-v1-20261030-3" },
            first.Puzzles.Select(puzzle => puzzle.Id));
        Assert.Same(first.Puzzles[0], service.GetCurrentById(first.Puzzles[0].Id));

        clock.Set(first.ExpiresAtUtc);
        var next = service.GetToday();
        Assert.NotSame(first, next);
        Assert.Equal(new DateOnly(2026, 10, 31), next.Date);
        Assert.Null(service.GetCurrentById(first.Puzzles[0].Id));
        Assert.Same(next.Puzzles[2], service.GetCurrentById("daily-v1-20261031-3"));

        // The old response remains a stable snapshot even though the service now holds the next day.
        Assert.Equal("daily-v1-20261030-1", first.Puzzles[0].Id);
        Assert.Equal(new DateOnly(2026, 10, 30), first.Date);
    }

    [Fact]
    public void EquivalentUtcInstantsAndSeparateServiceInstances_ProduceTheSameSet()
    {
        var utc = new TestClock(new DateTimeOffset(2026, 10, 31, 1, 0, 0, TimeSpan.Zero));
        var offset = new TestClock(new DateTimeOffset(2026, 10, 30, 18, 0, 0, TimeSpan.FromHours(-7)));

        var first = new DailyPuzzleService(utc).GetToday();
        var second = new DailyPuzzleService(offset).GetToday();

        Assert.Equal(new DateOnly(2026, 10, 31), second.Date);
        Assert.Equal(first.Puzzles.Select(Fingerprint), second.Puzzles.Select(Fingerprint));
        Assert.Equal(first.Puzzles.Select(RouteIdentity.GetCollectionKey), second.Puzzles.Select(RouteIdentity.GetCollectionKey));
    }

    [Fact]
    public async Task ConcurrentReads_ShareOneCompletedSetAndItsPuzzleReferences()
    {
        var service = new DailyPuzzleService(new TestClock(new DateTimeOffset(2026, 10, 30, 12, 0, 0, TimeSpan.Zero)));

        var sets = await Task.WhenAll(Enumerable.Range(0, 24).Select(_ => Task.Run(() => service.GetToday())));

        Assert.All(sets, set => Assert.Same(sets[0], set));
        Assert.All(sets, set => Assert.Same(sets[0].Puzzles[0], set.Puzzles[0]));
        Assert.Equal(new[] { 3, 4, 5 }, sets[0].Puzzles.Select(puzzle => puzzle.MaxMoves));
    }

    [Fact]
    public void Cache_DoesNotAccumulatePreviousDays()
    {
        var clock = new TestClock(new DateTimeOffset(2026, 10, 30, 12, 0, 0, TimeSpan.Zero));
        var service = new DailyPuzzleService(clock);
        var first = service.GetToday();
        clock.Set(clock.GetUtcNow().AddDays(1));
        service.GetToday();
        clock.Set(clock.GetUtcNow().AddDays(-1));

        var reconstructed = service.GetToday();

        Assert.NotSame(first, reconstructed);
        Assert.Equal(first.Puzzles.Select(Fingerprint), reconstructed.Puzzles.Select(Fingerprint));
    }

    [Fact]
    public void MidnightDuringPreparation_ReturnsTheNewDay()
    {
        var clock = new MidnightClock();

        var set = new DailyPuzzleService(clock).GetToday();

        Assert.Equal(new DateOnly(2026, 10, 31), set.Date);
        Assert.All(set.Puzzles, puzzle => Assert.StartsWith("daily-v1-20261031-", puzzle.Id));
    }

    [Fact]
    public void FutureAndPastIds_AreUnavailable()
    {
        var service = new DailyPuzzleService(new TestClock(new DateTimeOffset(2026, 10, 30, 12, 0, 0, TimeSpan.Zero)));

        Assert.Null(service.GetCurrentById("daily-v1-20261029-1"));
        Assert.Null(service.GetCurrentById("daily-v1-20261031-1"));
        Assert.Null(service.GetCurrentById("daily-v1-99991231-1"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("demo")]
    [InlineData("daily-v1-20261030-0")]
    [InlineData("daily-v1-20261030-4")]
    [InlineData("daily-v1-20261030-01")]
    [InlineData("daily-v1-20261030-1/anything")]
    [InlineData("daily-v1-20260229-1")]
    [InlineData("daily-v1-20261301-1")]
    [InlineData("DAILY-v1-20261030-1")]
    public void MalformedIds_AreRejectedBeforeReadingTheClockOrGenerating(string? id)
    {
        // A malformed ID has no reason to consult today's date or invoke any puzzle generation.
        var service = new DailyPuzzleService(new UnusableClock());

        Assert.Null(service.GetCurrentById(id!));
        Assert.False(DailyPuzzleService.TryParsePuzzleId(id, out _, out _));
    }

    [Fact]
    public void CancelledPreparation_DoesNotPoisonTheDailyCache()
    {
        var service = new DailyPuzzleService(new TestClock(new DateTimeOffset(2026, 10, 30, 12, 0, 0, TimeSpan.Zero)));

        Assert.Throws<OperationCanceledException>(() => service.GetToday(new CancellationToken(canceled: true)));
        var set = service.GetToday();

        Assert.Equal(3, set.Puzzles.Count);
        Assert.Same(set, service.GetToday());
    }

    [Fact]
    public void DailySamples_RespectAllThreeProfilesAndHaveDifferentCollectionsEachDay()
    {
        var clock = new TestClock(new DateTimeOffset(2026, 10, 1, 12, 0, 0, TimeSpan.Zero));
        var service = new DailyPuzzleService(clock);
        var collectionKeys = new HashSet<string>();
        PuzzleGenerationProfile[] profiles =
        {
            PuzzleGenerationProfile.ThreeMoves,
            PuzzleGenerationProfile.FourMoves,
            PuzzleGenerationProfile.FiveMoves
        };

        for (int day = 0; day < 31; day++)
        {
            var set = service.GetToday();
            for (int index = 0; index < profiles.Length; index++)
            {
                var puzzle = set.Puzzles[index];
                var profile = profiles[index];
                Assert.Equal(profile.MaxMoves, puzzle.MaxMoves);
                Assert.Equal(profile.HandSize, puzzle.Operations.Count);
                Assert.InRange(puzzle.MinimumMoves, profile.MinimumSolutionMoves, profile.MaximumSolutionMoves);
                Assert.InRange(puzzle.TotalRoutes, profile.MinimumRoutes, profile.MaximumRoutes);
                Assert.True(collectionKeys.Add(RouteIdentity.GetCollectionKey(puzzle)));
            }
            clock.Set(clock.GetUtcNow().AddDays(1));
        }
    }

    [Fact]
    public void Set_CopiesItsInputAndRejectsAnInvalidDailyShape()
    {
        var original = new DailyPuzzleService(new TestClock(new DateTimeOffset(2026, 10, 30, 12, 0, 0, TimeSpan.Zero))).GetToday();
        var source = original.Puzzles.ToList();
        var copied = new DailyPuzzleSet(original.Date, source);
        source.Clear();

        Assert.Equal(3, copied.Puzzles.Count);
        Assert.Throws<NotSupportedException>(() => ((ICollection<PuzzleDefinition>)copied.Puzzles).Clear());
        Assert.Throws<ArgumentException>(() => new DailyPuzzleSet(original.Date, Array.Empty<PuzzleDefinition>()));
        Assert.Throws<ArgumentException>(() => new DailyPuzzleSet(original.Date, original.Puzzles.Reverse().ToArray()));
    }

    private static string Fingerprint(PuzzleDefinition puzzle) =>
        $"{puzzle.Id}|{puzzle.Puzzle.StartNumber}|{puzzle.Puzzle.TargetNumber}|{puzzle.MaxMoves}|{puzzle.MinimumMoves}|{puzzle.TotalRoutes}|"
        + string.Join(",", puzzle.Operations.Select(card => card.Description));

    private sealed class TestClock(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset _now = now;
        public override DateTimeOffset GetUtcNow() => _now;
        public void Set(DateTimeOffset value) => _now = value;
    }

    private sealed class MidnightClock : TimeProvider
    {
        private int _reads;
        public override DateTimeOffset GetUtcNow() => Interlocked.Increment(ref _reads) == 1
            ? new DateTimeOffset(2026, 10, 30, 23, 59, 59, TimeSpan.Zero)
            : new DateTimeOffset(2026, 10, 31, 0, 0, 0, TimeSpan.Zero);
    }

    private sealed class UnusableClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => throw new InvalidOperationException("The clock should not be read for a malformed ID.");
    }
}
