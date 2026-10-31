using Microsoft.AspNetCore.Mvc;
using NumberForge.Api.Contracts;
using NumberForge.Api.Controllers;
using NumberForge.Api.Services;
using NumberForge.Core;

namespace NumberForge.Tests;

public class SessionTimingTests
{
    [Fact]
    public async Task Timing_StaysHiddenUntilWinAndRemainsFixedAcrossReadsAndRejectedMoves()
    {
        var clock = new TestClock();
        var controller = new SessionController(new GameSessionStore(clock), new PuzzleCatalog()).ForPlayer();
        var created = Created(await controller.Create());
        Assert.Null(created.ElapsedSeconds);

        clock.Advance(TimeSpan.FromSeconds(12.5));
        Assert.Null(State(await controller.GetById(created.SessionId)).ElapsedSeconds);
        var firstMove = State(await controller.ApplyMove(created.SessionId, new ApplyMoveRequest(1)));
        Assert.Null(firstMove.ElapsedSeconds);

        clock.Advance(TimeSpan.FromSeconds(20.75));
        var won = State(await controller.ApplyMove(created.SessionId, new ApplyMoveRequest(2)));
        Assert.Equal(14, won.CurrentNumber);
        Assert.Equal(33.25, won.ElapsedSeconds);

        // Reopening the finished game much later must return the original finishing time.
        clock.Advance(TimeSpan.FromHours(1));
        Assert.Equal(33.25, State(await controller.GetById(created.SessionId)).ElapsedSeconds);
        Assert.Equal(409, Assert.IsType<ObjectResult>(
            await controller.ApplyMove(created.SessionId, new ApplyMoveRequest(3))).StatusCode);
        Assert.Equal(33.25, State(await controller.GetById(created.SessionId)).ElapsedSeconds);

        // Responses already sent during play remain snapshots with no disclosed duration.
        Assert.Null(created.ElapsedSeconds);
        Assert.Null(firstMove.ElapsedSeconds);
        Assert.Equal(33.25, won.ElapsedSeconds);
    }

    [Theory]
    [InlineData(5, 3, false)]  // Win with a card and moves still available.
    [InlineData(99, 1, false)] // Lose by exhausting the move budget.
    [InlineData(99, 3, true)]  // Lose by using the only card before the move limit.
    public async Task FinishingMove_FreezesTimeForEveryEndCondition(int target, int maxMoves, bool oneCard)
    {
        var clock = new TestClock();
        var store = new GameSessionStore(clock);
        var controller = new SessionController(store, new PuzzleCatalog()).ForPlayer();
        IMathOperation[] cards = oneCard
            ? new IMathOperation[] { new MathOperation(2) }
            : new IMathOperation[] { new MathOperation(2), new MathOperation(4) };
        Guid id = (await store.AddAsync(new GameSession(new Puzzle(3, target), maxMoves, cards), TestPlayer.Id))!.Value;

        clock.Advance(TimeSpan.FromSeconds(7.25));
        var result = State(await controller.ApplyMove(id, new ApplyMoveRequest(1)));
        Assert.Equal(7.25, result.ElapsedSeconds);
        Assert.True((await store.GetAsync(id, TestPlayer.Id))!.Game.IsFinished);

        clock.Advance(TimeSpan.FromMinutes(5));
        Assert.Equal(7.25, State(await controller.GetById(id)).ElapsedSeconds);
    }

    [Fact]
    public async Task NewAttempt_StartsItsOwnTimerWithoutChangingThePreviousResult()
    {
        var clock = new TestClock();
        var controller = new SessionController(new GameSessionStore(clock), new PuzzleCatalog()).ForPlayer();
        var first = Created(await controller.Create());
        clock.Advance(TimeSpan.FromSeconds(5));
        State(await controller.ApplyMove(first.SessionId, new ApplyMoveRequest(1)));
        Assert.Equal(5, State(await controller.ApplyMove(first.SessionId, new ApplyMoveRequest(2))).ElapsedSeconds);

        clock.Advance(TimeSpan.FromMinutes(2));
        var replay = Created(await controller.Create());
        Assert.NotEqual(first.SessionId, replay.SessionId);
        Assert.Null(replay.ElapsedSeconds);

        clock.Advance(TimeSpan.FromSeconds(6));
        State(await controller.ApplyMove(replay.SessionId, new ApplyMoveRequest(1)));
        Assert.Equal(6, State(await controller.ApplyMove(replay.SessionId, new ApplyMoveRequest(2))).ElapsedSeconds);
        Assert.Equal(5, State(await controller.GetById(first.SessionId)).ElapsedSeconds);
    }

    [Fact]
    public async Task RejectedMoves_DoNotStopTheActiveTimer()
    {
        var clock = new TestClock();
        var controller = new SessionController(new GameSessionStore(clock), new PuzzleCatalog()).ForPlayer();
        var created = Created(await controller.Create());

        clock.Advance(TimeSpan.FromSeconds(2));
        Assert.Equal(400, Assert.IsType<ObjectResult>(
            await controller.ApplyMove(created.SessionId, new ApplyMoveRequest(99))).StatusCode);
        Assert.Null(State(await controller.GetById(created.SessionId)).ElapsedSeconds);

        clock.Advance(TimeSpan.FromSeconds(3));
        State(await controller.ApplyMove(created.SessionId, new ApplyMoveRequest(1)));
        clock.Advance(TimeSpan.FromSeconds(4));
        Assert.Equal(409, Assert.IsType<ObjectResult>(
            await controller.ApplyMove(created.SessionId, new ApplyMoveRequest(1))).StatusCode);
        Assert.Null(State(await controller.GetById(created.SessionId)).ElapsedSeconds);

        clock.Advance(TimeSpan.FromSeconds(1));
        Assert.Equal(10, State(await controller.ApplyMove(created.SessionId, new ApplyMoveRequest(2))).ElapsedSeconds);
    }

    [Fact]
    public async Task WallClockAdjustment_DoesNotChangeTheMeasuredDuration()
    {
        var clock = new TestClock();
        var controller = new SessionController(new GameSessionStore(clock), new PuzzleCatalog()).ForPlayer();
        var created = Created(await controller.Create());

        clock.Advance(TimeSpan.FromSeconds(10));
        clock.AdjustUtc(TimeSpan.FromDays(-1));
        State(await controller.ApplyMove(created.SessionId, new ApplyMoveRequest(1)));
        Assert.Equal(10, State(await controller.ApplyMove(created.SessionId, new ApplyMoveRequest(2))).ElapsedSeconds);
    }

    [Fact]
    public async Task GameThatStartsFinished_HasAZeroSecondDuration()
    {
        var clock = new TestClock();
        var store = new GameSessionStore(clock);
        var controller = new SessionController(store, new PuzzleCatalog()).ForPlayer();
        Guid id = (await store.AddAsync(new GameSession(new Puzzle(3, 3), 2,
            new IMathOperation[] { new MathOperation(2) }), TestPlayer.Id))!.Value;

        clock.Advance(TimeSpan.FromMinutes(1));
        Assert.Equal(0, State(await controller.GetById(id)).ElapsedSeconds);
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

    private sealed class TestClock : TimeProvider
    {
        private DateTimeOffset _utcNow = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        private long _timestamp;

        public override DateTimeOffset GetUtcNow() => _utcNow;
        public override long GetTimestamp() => _timestamp;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;

        public void Advance(TimeSpan duration)
        {
            _utcNow += duration;
            _timestamp += duration.Ticks;
        }

        public void AdjustUtc(TimeSpan difference)
        {
            // Keep elapsed time advancing normally while simulating a wall-clock correction.
            _utcNow += difference;
        }
    }
}
