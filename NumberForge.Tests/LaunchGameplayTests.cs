using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using NumberForge.Api.Contracts;
using NumberForge.Api.Controllers;
using NumberForge.Api.Persistence;
using NumberForge.Api.Services;
using NumberForge.Core;

namespace NumberForge.Tests;

public sealed class LaunchGameplayTests
{
    [Fact]
    public async Task SessionIdDoesNotAuthorizeAnotherPlayer()
    {
        var setup = new Fixture();
        var created = await setup.Create();
        var other = new SessionController(setup.Store, setup.Catalog, setup.Clock).ForPlayer(TestPlayer.OtherId);
        Assert.IsType<NotFoundResult>(await other.GetById(created.SessionId));
        Assert.IsType<NotFoundResult>(await other.ApplyMove(created.SessionId, new(1)));
        var unchanged = await setup.Read(created.SessionId);
        Assert.Equal(0, unchanged.Revision);
        Assert.Empty(unchanged.History);
    }

    [Fact]
    public async Task WinningUnlocksOnlyThisPlayersCountsAndDeduplicatesTheirRoutes()
    {
        var f = new Fixture();
        Assert.Null((await f.Puzzle("demo")).TotalRoutes);
        var first = await f.Create();
        Assert.Null(first.TotalRoutes);
        Assert.All(first.AvailableOperations, op => Assert.Null(op.PreviewNumber));
        var win = await f.WinDemo(1, 2);
        Assert.Equal("won", win.Status);
        Assert.Equal(2, win.TotalRoutes);
        Assert.Equal(2, (await f.Puzzle("demo")).TotalRoutes);
        var otherPuzzles = new PuzzleController(f.Catalog, new(f.Store)).ForPlayer(TestPlayer.OtherId);
        Assert.Null(Assert.IsType<PuzzleResponse>(Assert.IsType<OkObjectResult>(await otherPuzzles.GetById("demo")).Value).TotalRoutes);
        await f.WinDemo(1, 2); // Replaying an already found route is allowed, but does not inflate discoveries.
        Assert.Equal(1, (await f.Progress("demo")).FoundRoutes);
        await f.WinDemo(2, 3);
        var progress = await f.Progress("demo");
        Assert.Equal(2, progress.FoundRoutes);
        Assert.Equal(2, progress.BestMoves);
        Assert.Equal(2, progress.RouteIds.Distinct().Count());
        var otherProgress = new ProgressController(f.Catalog, f.Store).ForPlayer(TestPlayer.OtherId);
        var hidden = Assert.IsType<PuzzleProgressResponse>(Assert.IsType<OkObjectResult>(await otherProgress.Get("demo")).Value);
        Assert.False(hidden.HasSolved);
        Assert.Null(hidden.TotalRoutes);
    }

    [Fact]
    public async Task RequestIdsMakeRetriesSafeAndRevisionRejectsStaleTabs()
    {
        var f = new Fixture();
        var start = await f.Create();
        var requestId = Guid.NewGuid();
        var accepted = Assert.IsType<SessionResponse>(Assert.IsType<OkObjectResult>(await f.Controller.ApplyMove(start.SessionId, new(1, 0, requestId))).Value);
        var retry = Assert.IsType<SessionResponse>(Assert.IsType<OkObjectResult>(await f.Controller.ApplyMove(start.SessionId, new(1, 0, requestId))).Value);
        Assert.Equal(accepted.Revision, retry.Revision);
        Assert.Single(retry.History);
        Assert.Equal(409, Assert.IsType<ObjectResult>(await f.Controller.ApplyMove(start.SessionId, new(2, 0))).StatusCode);
        Assert.Equal(409, Assert.IsType<ObjectResult>(await f.Controller.ApplyMove(start.SessionId, new(2, 1, requestId))).StatusCode);
        var winRequest = new ApplyMoveRequest(2, 1, Guid.NewGuid());
        var win = Assert.IsType<SessionResponse>(Assert.IsType<OkObjectResult>(await f.Controller.ApplyMove(start.SessionId, winRequest)).Value);
        f.Clock.Advance(TimeSpan.FromHours(1));
        var winRetry = Assert.IsType<SessionResponse>(Assert.IsType<OkObjectResult>(await f.Controller.ApplyMove(start.SessionId, winRequest)).Value);
        Assert.Equal(win.ElapsedSeconds, winRetry.ElapsedSeconds);
        Assert.Equal(win.RouteId, winRetry.RouteId);
        Assert.Equal(2, winRetry.MovesUsed);
        Assert.Equal(1, (await f.Progress("demo")).FoundRoutes);
    }

    [Fact]
    public async Task DailyCutoffStopsNewMovesAndReplaysButKeepsResultsReadable()
    {
        var f = new Fixture();
        var daily = f.Daily.GetToday();
        var start = await f.Create(daily.Puzzles[0].Id);
        Assert.Equal(daily.ExpiresAtUtc, start.PlayUntilUtc);
        var winningRoute = FindRoute(daily.Puzzles[1]);
        var completed = await f.Create(daily.Puzzles[1].Id);
        foreach (int card in winningRoute) completed = await f.Move(completed.SessionId, card);
        Assert.Equal("won", completed.Status);
        f.Clock.Advance(daily.ExpiresAtUtc - f.Clock.GetUtcNow());
        Assert.Equal(410, Assert.IsType<ObjectResult>(await f.Controller.ApplyMove(start.SessionId, new(1))).StatusCode);
        var expired = await f.Read(start.SessionId);
        Assert.Equal("expired", expired.Status);
        Assert.Empty(expired.History);
        Assert.Null(expired.ElapsedSeconds);
        Assert.Equal(404, Assert.IsType<ObjectResult>(await f.Controller.Create(daily.Puzzles[0].Id)).StatusCode);
        Assert.Equal("won", (await f.Read(completed.SessionId)).Status);
        Assert.IsType<NotFoundResult>(await new ProgressController(f.Catalog, f.Store).ForPlayer().Get(daily.Puzzles[1].Id));
        Assert.Equal(7, f.Catalog.All.Count);
        Assert.DoesNotContain(f.Catalog.All, p => p.Id == daily.Puzzles[0].Id);
        Assert.Null((await f.Puzzle(f.Daily.GetToday().Puzzles[0].Id)).TotalRoutes);
    }

    [Fact]
    public async Task StoredHistoryRestoresRulesAndTimerWithoutProcessMemory()
    {
        var f = new Fixture();
        var first = await f.Create();
        f.Clock.Advance(TimeSpan.FromSeconds(5));
        await f.Move(first.SessionId, 1);
        // A fresh service has no cached GameSession or monotonic timer. It reads the durable document.
        var recoveredStore = new GameSessionStore(f.Clock, f.Repository, Options.Create(new SessionLimits()));
        var recovered = new SessionController(recoveredStore, f.Catalog, f.Clock).ForPlayer();
        f.Clock.Advance(TimeSpan.FromSeconds(7));
        var win = Assert.IsType<SessionResponse>(Assert.IsType<OkObjectResult>(await recovered.ApplyMove(first.SessionId, new(2))).Value);
        Assert.Equal(14, win.CurrentNumber);
        Assert.True(win.Charm!.IsUsed);
        Assert.Equal(12d, win.ElapsedSeconds);
        Assert.Equal(2, win.History.Count);
        Assert.Equal(1, (await f.Progress("demo")).FoundRoutes);
    }

    [Fact]
    public async Task AdmissionLimitsAndExpiryBoundStoredAttempts()
    {
        var clock = new Clock();
        var store = new GameSessionStore(clock, new InMemoryGameStateRepository(), Options.Create(new SessionLimits { MaxSessionsPerPlayer = 2, MaxSessionsTotal = 3 }));
        var definition = new PuzzleCatalog(new DailyPuzzleService(clock)).Get("demo")!;
        Guid? first = await store.AddAsync(new(definition), TestPlayer.Id);
        Assert.NotNull(first);
        Assert.NotNull(await store.AddAsync(new(definition), TestPlayer.Id));
        Assert.Null(await store.AddAsync(new(definition), TestPlayer.Id));
        Assert.NotNull(await store.AddAsync(new(definition), TestPlayer.OtherId));
        Assert.Null(await store.AddAsync(new(definition), TestPlayer.OtherId));
        clock.Advance(TimeSpan.FromDays(1));
        Assert.Null(await store.GetAsync(first.Value, TestPlayer.Id));
        Assert.NotNull(await store.AddAsync(new(definition), TestPlayer.Id));
    }

    [Fact]
    public async Task InvalidAndRejectedMovesDoNotChangeTheSavedDocument()
    {
        var f = new Fixture();
        var start = await f.Create();
        Assert.Equal(400, Assert.IsType<ObjectResult>(await f.Controller.ApplyMove(start.SessionId, new(99))).StatusCode);
        Assert.Equal(0, (await f.Read(start.SessionId)).Revision);
        await f.Move(start.SessionId, 1);
        Assert.Equal(409, Assert.IsType<ObjectResult>(await f.Controller.ApplyMove(start.SessionId, new(1))).StatusCode);
        Assert.Equal(1, (await f.Read(start.SessionId)).Revision);
    }

    private static int[] FindRoute(PuzzleDefinition definition)
    {
        int[]? Search(int[] path)
        {
            var game = new GameSession(definition);
            foreach (int id in path) if (!game.TryApplyOperation(game.StartingOperations[id - 1])) return null;
            if (game.Puzzle.IsTargetReached(game.CurrentNumber)) return path;
            if (game.IsFinished) return null;
            foreach (int id in Enumerable.Range(1, definition.Operations.Count).Except(path))
                if (Search([.. path, id]) is { } result) return result;
            return null;
        }
        return Search([]) ?? throw new InvalidOperationException("Daily puzzle should be solvable.");
    }

    private sealed class Fixture
    {
        public Clock Clock { get; } = new();
        public InMemoryGameStateRepository Repository { get; } = new();
        public GameSessionStore Store { get; }
        public DailyPuzzleService Daily { get; }
        public PuzzleCatalog Catalog { get; }
        public SessionController Controller { get; }
        public Fixture()
        {
            Daily = new(Clock); Catalog = new(Daily);
            Store = new(Clock, Repository, Options.Create(new SessionLimits()));
            Controller = new SessionController(Store, Catalog, Clock).ForPlayer();
        }
        public async Task<SessionResponse> Create(string id = "demo") => Assert.IsType<SessionResponse>(Assert.IsType<CreatedAtActionResult>(await Controller.Create(id)).Value);
        public async Task<SessionResponse> Read(Guid id) => Assert.IsType<SessionResponse>(Assert.IsType<OkObjectResult>(await Controller.GetById(id)).Value);
        public async Task<SessionResponse> Move(Guid id, int card) => Assert.IsType<SessionResponse>(Assert.IsType<OkObjectResult>(await Controller.ApplyMove(id, new(card))).Value);
        public async Task<SessionResponse> WinDemo(int first, int second) { var game = await Create(); await Move(game.SessionId, first); return await Move(game.SessionId, second); }
        public async Task<PuzzleResponse> Puzzle(string id) => Assert.IsType<PuzzleResponse>(Assert.IsType<OkObjectResult>(await new PuzzleController(Catalog, new(Store)).ForPlayer().GetById(id)).Value);
        public async Task<PuzzleProgressResponse> Progress(string id) => Assert.IsType<PuzzleProgressResponse>(Assert.IsType<OkObjectResult>(await new ProgressController(Catalog, Store).ForPlayer().Get(id)).Value);
    }
    private sealed class Clock : TimeProvider
    {
        private DateTimeOffset _now = new(2026, 10, 30, 12, 0, 0, TimeSpan.Zero);
        private long _ticks;
        public override DateTimeOffset GetUtcNow() => _now;
        public override long GetTimestamp() => _ticks;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public void Advance(TimeSpan time) { _now += time; _ticks += time.Ticks; }
    }
}
