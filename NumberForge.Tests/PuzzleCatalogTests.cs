using Microsoft.AspNetCore.Mvc;
using NumberForge.Api.Contracts;
using NumberForge.Api.Controllers;
using NumberForge.Api.Services;
using NumberForge.Core;

namespace NumberForge.Tests;

public class PuzzleCatalogTests
{
    private const string GeneratedPuzzleId = "generated-v1-42-none";

    [Fact]
    public async Task GetAll_ReturnsFourPracticePuzzlesAndTodaysFixedSetWithoutSpoilers()
    {
        var catalog = new PuzzleCatalog();
        var controller = new PuzzleController(catalog, new PuzzleResponseMapper(new GameSessionStore())).ForPlayer();

        var result = Assert.IsType<OkObjectResult>(await controller.GetAll());
        var puzzles = Assert.IsType<PuzzleResponse[]>(result.Value);

        Assert.Equal(7, puzzles.Length);
        Assert.Equal(new[] { "demo", "detour", "shortcut", GeneratedPuzzleId }, puzzles.Take(4).Select(puzzle => puzzle.Id));
        Assert.Equal(new[] { "First Forge", "The Detour", "Find the Shortcut", "Generated Forge" },
            puzzles.Take(4).Select(puzzle => puzzle.Name));
        Assert.Equal(new[] { 3, 4, 5 }, puzzles.Skip(4).Select(puzzle => puzzle.MaxMoves));
        Assert.All(puzzles, puzzle => Assert.Null(puzzle.TotalRoutes));
        Assert.All(puzzles, puzzle => Assert.All(puzzle.Operations, card => Assert.Null(card.PreviewNumber)));
    }

    [Theory]
    [InlineData("demo")]
    [InlineData("detour")]
    [InlineData("shortcut")]
    [InlineData(GeneratedPuzzleId)]
    public async Task Create_WithSelectedPuzzle_MatchesItsPreviewAndRetainsIdentityOnGet(string puzzleId)
    {
        var catalog = new PuzzleCatalog();
        var puzzleController = new PuzzleController(catalog, new PuzzleResponseMapper(new GameSessionStore())).ForPlayer();
        var sessionController = new SessionController(new GameSessionStore(), catalog).ForPlayer();
        var preview = Assert.IsType<PuzzleResponse>(
            Assert.IsType<OkObjectResult>(await puzzleController.GetById(puzzleId)).Value);

        var createdResult = Assert.IsType<CreatedAtActionResult>(await sessionController.Create(puzzleId));
        Assert.Equal(201, createdResult.StatusCode);
        var created = Assert.IsType<SessionResponse>(createdResult.Value);

        Assert.Equal(puzzleId, created.PuzzleId);
        Assert.Equal(preview.Name, created.PuzzleName);
        Assert.Equal(preview.StartNumber, created.CurrentNumber);
        Assert.Equal(preview.TargetNumber, created.TargetNumber);
        Assert.Equal(preview.MaxMoves, created.MaxMoves);
        Assert.Equal(preview.TotalRoutes, created.TotalRoutes);
        Assert.Equal(preview.CollectionKey, created.CollectionKey);
        Assert.Equal(preview.Operations.Select(card => (card.Id, card.Description)),
            created.AvailableOperations.Select(card => (card.Id, card.Description)));
        Assert.Equal(catalog.Get(puzzleId)!.Charm == CharmType.Echo, created.Charm is not null);

        // A browser that only saved the session ID can recover the correct puzzle after reload.
        var restored = Assert.IsType<SessionResponse>(
            Assert.IsType<OkObjectResult>(await sessionController.GetById(created.SessionId)).Value);
        Assert.Equal(puzzleId, restored.PuzzleId);
        Assert.Equal(preview.Name, restored.PuzzleName);
        Assert.Equal(preview.TotalRoutes, restored.TotalRoutes);
        Assert.Equal(preview.CollectionKey, restored.CollectionKey);
        Assert.Equal(0, restored.MovesUsed);
        Assert.Empty(restored.History);
    }

    [Theory]
    [InlineData(3, true)]
    [InlineData(4, false)]
    public async Task GeneratedPuzzle_CanBeWonRestoredAndReplayedWithStableRouteIdentity(int moves, bool isPerfect)
    {
        var catalog = new PuzzleCatalog();
        var definition = catalog.Get(GeneratedPuzzleId)!;
        var controller = new SessionController(new GameSessionStore(), catalog).ForPlayer();

        // Find an actual winning route, then play it through the same controller actions as the browser.
        var route = FindSolutions(definition).First(game => game.MovesUsed == moves)
            .History.Select(move => move.OperationId).ToArray();
        var won = await PlayGeneratedRoute(controller, route);

        Assert.Equal(GeneratedPuzzleId, won.PuzzleId);
        Assert.Equal("Generated Forge", won.PuzzleName);
        Assert.Equal(won.TargetNumber, won.CurrentNumber);
        Assert.Equal(moves, won.MovesUsed);
        Assert.Equal(isPerfect, won.IsPerfect);
        Assert.NotNull(won.ElapsedSeconds);
        Assert.Equal(definition.TotalRoutes, won.TotalRoutes);
        Assert.Equal(RouteIdentity.GetCollectionKey(definition), won.CollectionKey);
        Assert.NotNull(won.RouteId);

        var restored = Assert.IsType<SessionResponse>(
            Assert.IsType<OkObjectResult>(await controller.GetById(won.SessionId)).Value);
        Assert.Equal(won.RouteId, restored.RouteId);
        Assert.Equal(won.IsPerfect, restored.IsPerfect);
        Assert.Equal(won.ElapsedSeconds, restored.ElapsedSeconds);

        // Replaying creates fresh progress on the same puzzle, preserving the completed attempt.
        var replay = Assert.IsType<SessionResponse>(Assert.IsType<CreatedAtActionResult>(await controller.Create(GeneratedPuzzleId)).Value);
        Assert.NotEqual(won.SessionId, replay.SessionId);
        Assert.Equal(won.PuzzleId, replay.PuzzleId);
        Assert.Equal(won.CollectionKey, replay.CollectionKey);
        Assert.Equal(definition.Puzzle.StartNumber, replay.CurrentNumber);
        Assert.Equal(5, replay.AvailableOperations.Count);
        Assert.Empty(replay.History);
        Assert.Equal(0, replay.MovesUsed);
        Assert.Null(replay.IsPerfect);
        Assert.Null(replay.RouteId);
        Assert.Null(replay.ElapsedSeconds);

        // Rebuilding the catalog regenerates the same challenge, keeping saved route discoveries compatible.
        var rebuilt = new SessionController(new GameSessionStore(), new PuzzleCatalog()).ForPlayer();
        var repeatedWin = await PlayGeneratedRoute(rebuilt, route);
        Assert.Equal(won.CollectionKey, repeatedWin.CollectionKey);
        Assert.Equal(won.RouteId, repeatedWin.RouteId);
    }

    [Fact]
    public async Task UnknownPuzzle_IsRejectedByBothControllers()
    {
        var catalog = new PuzzleCatalog();
        var puzzleController = new PuzzleController(catalog, new PuzzleResponseMapper(new GameSessionStore())).ForPlayer();
        var sessionController = new SessionController(new GameSessionStore(), catalog).ForPlayer();

        Assert.IsType<NotFoundResult>(await puzzleController.GetById("missing"));
        var result = Assert.IsType<ObjectResult>(await sessionController.Create("missing"));
        Assert.Equal(404, result.StatusCode);
        Assert.Equal("Puzzle not found", Assert.IsType<ProblemDetails>(result.Value).Title);
    }

    [Fact]
    public void Sessions_FromTheSameDefinition_KeepSeparateHandsHistoryAndCharmState()
    {
        var definition = new PuzzleCatalog().Get("demo")!;
        var first = new GameSession(definition);
        var second = new GameSession(definition);

        Assert.True(first.TryApplyOperation(definition.Operations[0]));

        Assert.Equal(7, first.CurrentNumber);
        Assert.True(first.EchoUsed);
        Assert.Equal(2, first.AvailableOperations.Count);
        Assert.Equal(3, second.CurrentNumber);
        Assert.False(second.EchoUsed);
        Assert.Equal(0, second.MovesUsed);
        Assert.Empty(second.History);
        Assert.Equal(definition.Operations, second.AvailableOperations);
        Assert.Equal(3, definition.Operations.Count);
        Assert.Same(definition, first.Definition);
        Assert.Same(definition, second.Definition);
    }

    [Fact]
    public void Definition_CopiesItsHandAndExposesAReadOnlyCollection()
    {
        var card = new MathOperation(2);
        var source = new List<IMathOperation> { card };
        var definition = new PuzzleDefinition("test", "Test", new Puzzle(3, 5), 1, 1, 1, source);

        source.Clear();

        Assert.Same(card, Assert.Single(definition.Operations));
        var collection = Assert.IsAssignableFrom<ICollection<IMathOperation>>(definition.Operations);
        Assert.Throws<NotSupportedException>(() => collection.Clear());
        Assert.Same(card, Assert.Single(new GameSession(definition).AvailableOperations));
    }

    [Theory]
    [InlineData("demo", 2, 2, 2)]
    [InlineData("detour", 3, 3, 1)]
    [InlineData("shortcut", 3, 4, 19)]
    public void CatalogPuzzle_HasTheIntendedSolutionLengths(string puzzleId, int shortest, int longest, int totalRoutes)
    {
        var definition = new PuzzleCatalog().Get(puzzleId)!;
        var solutions = FindSolutions(definition).ToArray();

        Assert.NotEmpty(solutions);
        Assert.Equal(totalRoutes, solutions.Length);
        Assert.Equal(solutions.Length, definition.TotalRoutes);
        Assert.Equal(totalRoutes, solutions.Select(RouteIdentity.GetWinningRouteId).Distinct().Count());
        Assert.Equal(shortest, solutions.Min(game => game.MovesUsed));
        // Scoring metadata must match an exhaustive search, rather than an estimated route length.
        Assert.Equal(shortest, definition.MinimumMoves);
        Assert.Equal(longest, solutions.Max(game => game.MovesUsed));
        Assert.All(solutions, game => Assert.Equal(game.MovesUsed == shortest, game.IsPerfect));
    }

    [Fact]
    public void Detour_AllWinningRoutesGoAboveTheTarget()
    {
        var definition = new PuzzleCatalog().Get("detour")!;
        var solutions = FindSolutions(definition).ToArray();

        Assert.NotEmpty(solutions);
        Assert.All(solutions, game =>
            Assert.Contains(game.History, move => move.AfterNumber > definition.Puzzle.TargetNumber));
    }

    private static async Task<SessionResponse> PlayGeneratedRoute(SessionController controller, int[] cardIds)
    {
        var createdResult = Assert.IsType<CreatedAtActionResult>(await controller.Create(GeneratedPuzzleId));
        Assert.Equal(201, createdResult.StatusCode);
        var current = Assert.IsType<SessionResponse>(createdResult.Value);
        foreach (int cardId in cardIds)
        {
            current = Assert.IsType<SessionResponse>(Assert.IsType<OkObjectResult>(
                await controller.ApplyMove(current.SessionId, new ApplyMoveRequest(cardId))).Value);
        }
        return current;
    }

    private static IEnumerable<GameSession> FindSolutions(PuzzleDefinition definition)
    {
        // Explore the small hands using the real rules, including Echo and early wins.
        var routes = new Queue<int[]>();
        routes.Enqueue(Array.Empty<int>());
        while (routes.Count > 0)
        {
            var route = routes.Dequeue();
            var game = new GameSession(definition);
            foreach (int cardIndex in route)
            {
                Assert.True(game.TryApplyOperation(definition.Operations[cardIndex]));
            }

            if (game.Puzzle.IsTargetReached(game.CurrentNumber))
            {
                yield return game;
                continue;
            }
            if (route.Length >= definition.MaxMoves)
            {
                continue;
            }

            for (int index = 0; index < definition.Operations.Count; index++)
            {
                if (!route.Contains(index))
                {
                    routes.Enqueue(route.Append(index).ToArray());
                }
            }
        }
    }
}
