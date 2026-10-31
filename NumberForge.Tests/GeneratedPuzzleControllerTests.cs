using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using NumberForge.Api.Contracts;
using NumberForge.Api.Controllers;
using NumberForge.Api.Services;
using NumberForge.Core;

namespace NumberForge.Tests;

public class GeneratedPuzzleControllerTests
{
    [Fact]
    public async Task CreateGenerated_ReturnsGoneWithoutGrowingThePuzzleSelector()
    {
        var catalog = new PuzzleCatalog(new DailyPuzzleService(new TestClock()));
        var controller = new PuzzleController(catalog, new PuzzleResponseMapper(new GameSessionStore())).ForPlayer();
        var originalIds = catalog.All.Select(puzzle => puzzle.Id).ToArray();

        for (int request = 0; request < 20; request++)
        {
            var response = Assert.IsType<ObjectResult>(controller.CreateGenerated());
            Assert.Equal(410, response.StatusCode);
            Assert.Equal(410, Assert.IsType<ProblemDetails>(response.Value).Status);
        }

        var listed = Assert.IsType<PuzzleResponse[]>(Assert.IsType<OkObjectResult>(await controller.GetAll()).Value);
        Assert.Equal(7, listed.Length);
        Assert.Equal(originalIds, listed.Select(puzzle => puzzle.Id));
        Assert.All(listed, puzzle =>
        {
            Assert.Null(puzzle.TotalRoutes);
            Assert.All(puzzle.Operations, card => Assert.Null(card.PreviewNumber));
        });
        Assert.DoesNotContain("\"minimumMoves\"",
            JsonSerializer.Serialize(listed, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public async Task CurrentDailyPuzzle_CanBeSelectedWonRestoredAndReplayed(int slot)
    {
        var clock = new TestClock();
        var daily = new DailyPuzzleService(clock);
        var catalog = new PuzzleCatalog(daily);
        var definition = daily.GetToday().Puzzles[slot - 1];
        var store = new GameSessionStore(clock);
        var puzzles = new PuzzleController(catalog, new PuzzleResponseMapper(store)).ForPlayer();
        var sessions = new SessionController(store, catalog, clock).ForPlayer();
        var preview = Assert.IsType<PuzzleResponse>(Assert.IsType<OkObjectResult>(await puzzles.GetById(definition.Id)).Value);
        Assert.Null(preview.TotalRoutes);
        Assert.Equal(slot + 2, preview.MaxMoves);

        var created = Assert.IsType<SessionResponse>(Assert.IsType<CreatedAtActionResult>(await sessions.Create(definition.Id)).Value);
        Assert.Equal(preview.CollectionKey, created.CollectionKey);
        Assert.Equal(preview.Operations.Select(card => card.Description), created.AvailableOperations.Select(card => card.Description));
        Assert.Null(created.TotalRoutes);
        var current = created;
        foreach (int cardId in FindShortestRoute(definition))
        {
            current = Assert.IsType<SessionResponse>(Assert.IsType<OkObjectResult>(
                await sessions.ApplyMove(created.SessionId, new ApplyMoveRequest(cardId))).Value);
        }

        Assert.Equal(current.TargetNumber, current.CurrentNumber);
        Assert.True(current.IsPerfect);
        Assert.Equal(definition.MinimumMoves, current.MovesUsed);
        Assert.Equal(definition.TotalRoutes, current.TotalRoutes);
        Assert.NotNull(current.RouteId);
        var restored = Assert.IsType<SessionResponse>(Assert.IsType<OkObjectResult>(await sessions.GetById(created.SessionId)).Value);
        Assert.Equal(definition.Id, restored.PuzzleId);
        Assert.Equal(current.RouteId, restored.RouteId);
        Assert.Equal(current.ElapsedSeconds, restored.ElapsedSeconds);

        // A verified win unlocks route hunting for this player's next attempt and puzzle details.
        var unlocked = Assert.IsType<PuzzleResponse>(Assert.IsType<OkObjectResult>(await puzzles.GetById(definition.Id)).Value);
        Assert.Equal(definition.TotalRoutes, unlocked.TotalRoutes);
        var replay = Assert.IsType<SessionResponse>(Assert.IsType<CreatedAtActionResult>(await sessions.Create(definition.Id)).Value);
        Assert.NotEqual(created.SessionId, replay.SessionId);
        Assert.Equal(definition.Id, replay.PuzzleId);
        Assert.Equal(preview.CollectionKey, replay.CollectionKey);
        Assert.Equal(definition.Puzzle.StartNumber, replay.CurrentNumber);
        Assert.Empty(replay.History);
        Assert.Equal(definition.Operations.Count, replay.AvailableOperations.Count);
        Assert.Null(replay.IsPerfect);
        Assert.Equal(definition.TotalRoutes, replay.TotalRoutes);
    }

    [Fact]
    public async Task ConcurrentGenerationRequests_CannotAppendChallenges()
    {
        var catalog = new PuzzleCatalog(new DailyPuzzleService(new TestClock()));
        var results = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Task.Run(() =>
            new PuzzleController(catalog, new PuzzleResponseMapper(new GameSessionStore())).ForPlayer().CreateGenerated())));

        Assert.All(results, result => Assert.Equal(410, Assert.IsType<ObjectResult>(result).StatusCode));
        Assert.Equal(7, catalog.All.Count);
        Assert.Equal(7, catalog.All.Select(puzzle => puzzle.Id).Distinct().Count());
    }

    private sealed class TestClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new DateTimeOffset(2026, 10, 30, 12, 0, 0, TimeSpan.Zero);
    }

    private static int[] FindShortestRoute(PuzzleDefinition definition)
    {
        var routes = new Queue<int[]>();
        routes.Enqueue(Array.Empty<int>());
        while (routes.Count > 0)
        {
            var route = routes.Dequeue();
            var game = new GameSession(definition);
            foreach (int cardId in route)
            {
                Assert.True(game.TryApplyOperation(definition.Operations[cardId - 1]));
            }
            if (game.Puzzle.IsTargetReached(game.CurrentNumber))
            {
                return route;
            }
            if (game.IsFinished)
            {
                continue;
            }
            for (int cardId = 1; cardId <= definition.Operations.Count; cardId++)
            {
                if (!route.Contains(cardId))
                {
                    routes.Enqueue(route.Append(cardId).ToArray());
                }
            }
        }
        throw new InvalidOperationException("The generated puzzle has no winning route.");
    }
}
