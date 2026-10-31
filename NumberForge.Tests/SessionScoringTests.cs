using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using NumberForge.Api.Contracts;
using NumberForge.Api.Controllers;
using NumberForge.Api.Services;
using NumberForge.Core;

namespace NumberForge.Tests;

public class SessionScoringTests
{
    [Theory]
    [InlineData("demo", new int[] { 1, 2 }, true)]
    [InlineData("detour", new int[] { 1, 2, 3 }, true)]
    [InlineData("shortcut", new int[] { 1, 3, 4 }, true)]
    [InlineData("shortcut", new int[] { 4, 1, 2, 3 }, false)]
    public async Task WinningRoute_ReportsItsGradeOnlyAfterWinningAndPreservesItOnRestore(
        string puzzleId, int[] cardIds, bool expectedPerfect)
    {
        var catalog = new PuzzleCatalog();
        var controller = new SessionController(new GameSessionStore(), catalog).ForPlayer();
        var created = Created(await controller.Create(puzzleId));
        var duringPlay = new List<SessionResponse> { created };
        Assert.Null(created.IsPerfect);

        var current = created;
        foreach (int cardId in cardIds)
        {
            current = State(await controller.ApplyMove(created.SessionId, new ApplyMoveRequest(cardId)));
            if (current.CurrentNumber != current.TargetNumber)
            {
                Assert.Null(current.IsPerfect);
                duringPlay.Add(current);
            }
        }

        Assert.Equal(current.TargetNumber, current.CurrentNumber);
        Assert.Equal(expectedPerfect, current.IsPerfect);
        Assert.NotNull(current.ElapsedSeconds);
        var restored = State(await controller.GetById(created.SessionId));
        Assert.Equal(expectedPerfect, restored.IsPerfect);
        Assert.Equal(current.ElapsedSeconds, restored.ElapsedSeconds);
        Assert.All(duringPlay, response => Assert.Null(response.IsPerfect));

        // JSON exposes the grade but never the minimum, even after a win.
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        Assert.DoesNotContain("\"minimumMoves\"", JsonSerializer.Serialize(restored, options));
        var previews = Assert.IsType<OkObjectResult>(await new PuzzleController(catalog, new PuzzleResponseMapper(new GameSessionStore())).ForPlayer().GetAll());
        Assert.DoesNotContain("\"minimumMoves\"", JsonSerializer.Serialize(previews.Value, options));

        // Replaying the same challenge starts without a grade and keeps the old result intact.
        var replay = Created(await controller.Create(puzzleId));
        Assert.NotEqual(created.SessionId, replay.SessionId);
        Assert.Null(replay.IsPerfect);
        Assert.Null(replay.ElapsedSeconds);
        Assert.Equal(expectedPerfect, State(await controller.GetById(created.SessionId)).IsPerfect);
    }

    [Fact]
    public async Task LosingAttempt_DoesNotReportAWinningGrade()
    {
        var controller = new SessionController(new GameSessionStore(), new PuzzleCatalog()).ForPlayer();
        var created = Created(await controller.Create());
        State(await controller.ApplyMove(created.SessionId, new ApplyMoveRequest(3)));

        var lost = State(await controller.ApplyMove(created.SessionId, new ApplyMoveRequest(1)));

        Assert.Equal(13, lost.CurrentNumber);
        Assert.Equal(2, lost.MovesUsed);
        Assert.Null(lost.IsPerfect);
        Assert.NotNull(lost.ElapsedSeconds);
        Assert.Null(State(await controller.GetById(created.SessionId)).IsPerfect);
    }

    [Fact]
    public async Task WinWithoutKnownMinimum_DoesNotInventAPerfectGrade()
    {
        var store = new GameSessionStore();
        var controller = new SessionController(store, new PuzzleCatalog()).ForPlayer();
        Guid id = (await store.AddAsync(new GameSession(new Puzzle(3, 5), 1,
            new IMathOperation[] { new MathOperation(2) }), TestPlayer.Id))!.Value;

        var won = State(await controller.ApplyMove(id, new ApplyMoveRequest(1)));

        Assert.Equal(won.TargetNumber, won.CurrentNumber);
        Assert.Null(won.IsPerfect);
        Assert.NotNull(won.ElapsedSeconds);
    }

    private static SessionResponse Created(IActionResult result)
    {
        var response = Assert.IsType<CreatedAtActionResult>(result);
        Assert.Equal(201, response.StatusCode);
        return Assert.IsType<SessionResponse>(response.Value);
    }

    private static SessionResponse State(IActionResult result)
    {
        return Assert.IsType<SessionResponse>(Assert.IsType<OkObjectResult>(result).Value);
    }
}
