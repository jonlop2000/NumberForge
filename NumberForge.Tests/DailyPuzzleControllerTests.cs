using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using NumberForge.Api.Contracts;
using NumberForge.Api.Controllers;
using NumberForge.Api.Services;

namespace NumberForge.Tests;

public class DailyPuzzleControllerTests
{
    [Fact]
    public async Task GetToday_ExposesTheThreePreviewsAndResetTimeWithoutSpoilers()
    {
        var controller = new DailyPuzzleController(new DailyPuzzleService(new TestClock()), new PuzzleResponseMapper(new GameSessionStore())).ForPlayer();

        var result = Assert.IsType<OkObjectResult>(await controller.GetToday());
        var response = Assert.IsType<DailyPuzzleSetResponse>(result.Value);

        Assert.Equal(new DateOnly(2026, 10, 30), response.Date);
        Assert.Equal(new DateTimeOffset(2026, 10, 31, 0, 0, 0, TimeSpan.Zero), response.ExpiresAtUtc);
        Assert.Equal(new[] { 3, 4, 5 }, response.Puzzles.Select(puzzle => puzzle.MaxMoves));
        Assert.All(response.Puzzles, puzzle =>
        {
            Assert.Null(puzzle.TotalRoutes);
            Assert.All(puzzle.Operations, card => Assert.Null(card.PreviewNumber));
            Assert.Equal(Enumerable.Range(1, puzzle.Operations.Count), puzzle.Operations.Select(card => card.Id));
        });

        var json = JsonSerializer.Serialize(response, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.DoesNotContain("minimumMoves", json);
        Assert.DoesNotContain("seed", json);
        Assert.DoesNotContain("history", json);
        Assert.DoesNotContain("routeId", json);
        Assert.Contains("\"totalRoutes\":null", json);
    }

    [Fact]
    public async Task Response_CopiesThePreviewList()
    {
        var controller = new DailyPuzzleController(new DailyPuzzleService(new TestClock()), new PuzzleResponseMapper(new GameSessionStore())).ForPlayer();
        var original = Assert.IsType<DailyPuzzleSetResponse>(Assert.IsType<OkObjectResult>(await controller.GetToday()).Value);
        var source = original.Puzzles.ToList();
        var response = new DailyPuzzleSetResponse(original.Date, original.AvailableFromUtc, original.ExpiresAtUtc, source);
        source.Clear();

        Assert.Equal(3, response.Puzzles.Count);
        Assert.Throws<NotSupportedException>(() => ((ICollection<PuzzleResponse>)response.Puzzles).Clear());
    }

    [Fact]
    public async Task CancelledRequest_DoesNotBecomeAGenerationFailure()
    {
        var controller = new DailyPuzzleController(new DailyPuzzleService(new TestClock()), new PuzzleResponseMapper(new GameSessionStore())).ForPlayer();

        await Assert.ThrowsAsync<OperationCanceledException>(() => controller.GetToday(new CancellationToken(canceled: true)));
    }

    private sealed class TestClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new DateTimeOffset(2026, 10, 30, 12, 0, 0, TimeSpan.Zero);
    }
}
