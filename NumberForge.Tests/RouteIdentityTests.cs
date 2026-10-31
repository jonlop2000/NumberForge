using Microsoft.AspNetCore.Mvc;
using NumberForge.Api.Contracts;
using NumberForge.Api.Controllers;
using NumberForge.Api.Services;
using NumberForge.Core;

namespace NumberForge.Tests;

public class RouteIdentityTests
{
    [Fact]
    public async Task WinningRoute_IsStableAcrossReplayRestoreAndSeparateCatalogInstances()
    {
        var firstController = new SessionController(new GameSessionStore(), new PuzzleCatalog()).ForPlayer();
        var secondController = new SessionController(new GameSessionStore(), new PuzzleCatalog()).ForPlayer();
        var first = await Win(firstController, 1, 2);
        var replay = await Win(secondController, 1, 2);
        var alternative = await Win(firstController, 2, 3);

        Assert.NotEqual(first.SessionId, replay.SessionId);
        Assert.Equal(first.CollectionKey, replay.CollectionKey);
        Assert.Equal(first.RouteId, replay.RouteId);
        Assert.NotEqual(first.RouteId, alternative.RouteId);
        Assert.Equal(2, first.TotalRoutes);
        Assert.Matches("^[a-f0-9]{64}$", first.RouteId!);
        Assert.Equal(first.RouteId, State(await firstController.GetById(first.SessionId)).RouteId);

        var preview = Assert.IsType<PuzzleResponse>(Assert.IsType<OkObjectResult>(
            await new PuzzleController(new PuzzleCatalog(), new PuzzleResponseMapper(new GameSessionStore())).ForPlayer().GetById("demo")).Value);
        Assert.Equal(preview.CollectionKey, first.CollectionKey);
        // A separate store has no verified discoveries, even when another player/store solved it.
        Assert.Null(preview.TotalRoutes);
    }

    [Fact]
    public async Task ActiveLostAndRejectedMoves_DoNotCreateWinningRouteIds()
    {
        var controller = new SessionController(new GameSessionStore(), new PuzzleCatalog()).ForPlayer();
        var created = Assert.IsType<SessionResponse>(Assert.IsType<CreatedAtActionResult>(await controller.Create()).Value);
        Assert.Null(created.RouteId);
        var first = State(await controller.ApplyMove(created.SessionId, new ApplyMoveRequest(1)));
        Assert.Null(first.RouteId);

        Assert.Equal(409, Assert.IsType<ObjectResult>(
            await controller.ApplyMove(created.SessionId, new ApplyMoveRequest(1))).StatusCode);
        Assert.Null(State(await controller.GetById(created.SessionId)).RouteId);

        var lost = State(await controller.ApplyMove(created.SessionId, new ApplyMoveRequest(3)));
        Assert.Equal(11, lost.CurrentNumber);
        Assert.Null(lost.RouteId);
        Assert.Null(created.RouteId);
        Assert.Null(first.RouteId);
    }

    [Fact]
    public void IdenticalCalculationsOnDifferentCards_KeepTheirOrderedIdentities()
    {
        var firstCard = new MathOperation(2);
        var secondCard = new MathOperation(2);
        var definition = new PuzzleDefinition("twins", "Twins", new Puzzle(3, 7), 2, 2, 2,
            new IMathOperation[] { firstCard, secondCard });
        var first = new GameSession(definition);
        var second = new GameSession(definition);
        Assert.True(first.TryApplyOperation(firstCard));
        Assert.True(first.TryApplyOperation(secondCard));
        Assert.True(second.TryApplyOperation(secondCard));
        Assert.True(second.TryApplyOperation(firstCard));

        Assert.Equal(new[] { 1, 2 }, first.History.Select(move => move.OperationId));
        Assert.Equal(new[] { 2, 1 }, second.History.Select(move => move.OperationId));
        Assert.Equal(first.History.Select(move => (move.BeforeNumber, move.AfterNumber)),
            second.History.Select(move => (move.BeforeNumber, move.AfterNumber)));
        Assert.NotEqual(RouteIdentity.GetWinningRouteId(first), RouteIdentity.GetWinningRouteId(second));
    }

    [Fact]
    public void CollectionKey_ChangesWithTheRulesButNotTheDisplayName()
    {
        var original = Definition("Original", 7, 2);
        var renamed = Definition("Renamed", 7, 2);
        var changedTarget = Definition("Original", 9, 2);
        var changedCard = Definition("Original", 7, 3);

        Assert.Equal(RouteIdentity.GetCollectionKey(original), RouteIdentity.GetCollectionKey(renamed));
        Assert.NotEqual(RouteIdentity.GetCollectionKey(original), RouteIdentity.GetCollectionKey(changedTarget));
        Assert.NotEqual(RouteIdentity.GetCollectionKey(original), RouteIdentity.GetCollectionKey(changedCard));
    }

    private static PuzzleDefinition Definition(string name, int target, int amount)
    {
        return new PuzzleDefinition("identity", name, new Puzzle(3, target), 2, 2, 1,
            new IMathOperation[] { new MathOperation(amount), new MathOperation(2) });
    }

    private static async Task<SessionResponse> Win(SessionController controller, params int[] cards)
    {
        var created = Assert.IsType<SessionResponse>(Assert.IsType<CreatedAtActionResult>(await controller.Create()).Value);
        var current = created;
        foreach (int card in cards)
        {
            current = State(await controller.ApplyMove(created.SessionId, new ApplyMoveRequest(card)));
        }
        Assert.Equal(current.TargetNumber, current.CurrentNumber);
        return current;
    }

    private static SessionResponse State(IActionResult result)
    {
        return Assert.IsType<SessionResponse>(Assert.IsType<OkObjectResult>(result).Value);
    }
}
