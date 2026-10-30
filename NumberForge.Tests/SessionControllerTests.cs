using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NumberForge.Api.Contracts;
using NumberForge.Api.Controllers;
using NumberForge.Api.Services;
using NumberForge.Core;

namespace NumberForge.Tests;

public class SessionControllerTests
{
    [Fact]
    public void ApplyMove_WithOriginalCardIds_ReachesTargetAndPreservesResponseSnapshots()
    {
        var store = new GameSessionStore();
        var controller = new SessionController(store);
        var created = CreatedState(controller.Create());

        // IDs stay attached to the original cards as the available hand shrinks.
        var firstMove = MoveState(controller.ApplyMove(created.SessionId, new ApplyMoveRequest(1)));
        AssertState(firstMove, 8, 1, 2, 3);

        var secondMove = MoveState(controller.ApplyMove(created.SessionId, new ApplyMoveRequest(2)));
        AssertState(secondMove, 16, 2, 3);

        var finalMove = MoveState(controller.ApplyMove(created.SessionId, new ApplyMoveRequest(3)));
        AssertState(finalMove, 12, 3);
        Assert.Equal(finalMove.TargetNumber, finalMove.CurrentNumber);
        var retrieved = MoveState(controller.GetById(created.SessionId));
        AssertState(retrieved, 12, 3);

        // Later moves must not change DTOs that were already returned to a client.
        AssertState(created, 3, 0, 1, 2, 3);
        AssertState(firstMove, 8, 1, 2, 3);

        // History is mapped in order, and earlier responses retain only the moves they captured.
        AssertHistory(created);
        AssertHistory(firstMove, (3, "Add 5", 8));
        AssertHistory(secondMove, (3, "Add 5", 8), (8, "Multiply by 2", 16));
        AssertHistory(finalMove, (3, "Add 5", 8), (8, "Multiply by 2", 16), (16, "Add -4", 12));
        AssertHistory(retrieved, (3, "Add 5", 8), (8, "Multiply by 2", 16), (16, "Add -4", 12));
    }

    [Fact]
    public void ApplyMove_WhenSessionDoesNotExist_ReturnsNotFound()
    {
        var controller = new SessionController(new GameSessionStore());

        var result = controller.ApplyMove(Guid.Empty, new ApplyMoveRequest(1));

        Assert.IsType<NotFoundResult>(result);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(4)]
    [InlineData(int.MaxValue)]
    public void ApplyMove_WhenCardIdIsInvalid_RejectsWithoutChangingState(int operationId)
    {
        var controller = new SessionController(new GameSessionStore());
        var created = CreatedState(controller.Create());

        var result = controller.ApplyMove(created.SessionId, new ApplyMoveRequest(operationId));

        AssertProblem(result, StatusCodes.Status400BadRequest);
        AssertState(MoveState(controller.GetById(created.SessionId)), 3, 0, 1, 2, 3);
    }

    [Fact]
    public void ApplyMove_WhenCardWasAlreadyUsed_ReturnsConflictWithoutChangingState()
    {
        var controller = new SessionController(new GameSessionStore());
        var created = CreatedState(controller.Create());
        MoveState(controller.ApplyMove(created.SessionId, new ApplyMoveRequest(1)));

        var result = controller.ApplyMove(created.SessionId, new ApplyMoveRequest(1));

        AssertProblem(result, StatusCodes.Status409Conflict);
        AssertState(MoveState(controller.GetById(created.SessionId)), 8, 1, 2, 3);
    }

    [Fact]
    public void ApplyMove_WhenTargetWasReached_RejectsAnUnusedCard()
    {
        var store = new GameSessionStore();
        var controller = new SessionController(store);
        var game = new GameSession(new Puzzle(3, 8), 3, DemoOperations());
        Guid id = store.Add(game);
        MoveState(controller.ApplyMove(id, new ApplyMoveRequest(1)));

        // Card 2 is unused and moves remain, so Core must reject because the target was reached.
        var result = controller.ApplyMove(id, new ApplyMoveRequest(2));

        AssertProblem(result, StatusCodes.Status409Conflict);
        AssertState(MoveState(controller.GetById(id)), 8, 1, 2, 3);
    }

    [Fact]
    public void ApplyMove_WhenMoveLimitWasReached_RejectsAnUnusedCard()
    {
        var store = new GameSessionStore();
        var controller = new SessionController(store);
        var game = new GameSession(new Puzzle(3, 12), 1, DemoOperations());
        Guid id = store.Add(game);
        MoveState(controller.ApplyMove(id, new ApplyMoveRequest(1)));

        // The target is not reached and card 2 remains, isolating the move-limit rule.
        var result = controller.ApplyMove(id, new ApplyMoveRequest(2));

        AssertProblem(result, StatusCodes.Status409Conflict);
        AssertState(MoveState(controller.GetById(id)), 8, 1, 2, 3);
    }

    [Fact]
    public async Task ApplyMove_WithOverlappingRequests_AppliesCardOnceAndSynchronizesReads()
    {
        var store = new GameSessionStore();
        var controller = new SessionController(store);
        using var operation = new PausingOperation();
        Guid id = store.Add(new GameSession(new Puzzle(3, 99), 2, new IMathOperation[] { operation }));
        var otherGame = CreatedState(controller.Create());

        // Pause the first operation inside Apply to create a controlled overlap between requests.
        var first = OnThread(() => new SessionController(store).ApplyMove(id, new ApplyMoveRequest(1)));
        var tasks = new List<Task> { first };
        try
        {
            Assert.True(operation.Entered.Wait(TimeSpan.FromSeconds(5)), "The first move did not start.");

            // Separate controller instances simulate separate HTTP requests sharing one store.
            var duplicate = OnThread(() => new SessionController(store).ApplyMove(id, new ApplyMoveRequest(1)));
            var read = OnThread(() => new SessionController(store).GetById(id));
            var independent = OnThread(() => new SessionController(store).ApplyMove(otherGame.SessionId, new ApplyMoveRequest(1)));
            tasks.Add(duplicate);
            tasks.Add(read);
            tasks.Add(independent);

            // Another game's move can finish while the first game's operation is paused.
            AssertState(MoveState(await independent.WaitAsync(TimeSpan.FromSeconds(5))), 8, 1, 2, 3);

            // Give the same-game requests a chance to reach the paused writer's lock.
            await Task.WhenAny(Task.WhenAll(duplicate, read), Task.Delay(200));
            operation.Release.Set();

            AssertState(MoveState(await first.WaitAsync(TimeSpan.FromSeconds(5))), 8, 1);
            AssertProblem(await duplicate.WaitAsync(TimeSpan.FromSeconds(5)), StatusCodes.Status409Conflict);
            AssertState(MoveState(await read.WaitAsync(TimeSpan.FromSeconds(5))), 8, 1);
            Assert.Equal(1, operation.ApplyCalls);
        }
        finally
        {
            // Release and join workers even when an assertion fails, keeping the test bounded.
            operation.Release.Set();
            await Task.WhenAll(tasks).WaitAsync(TimeSpan.FromSeconds(10));
        }
    }

    private static IMathOperation[] DemoOperations()
    {
        return new IMathOperation[]
        {
            new MathOperation(5),
            new MultiplyOperation(2),
            new MathOperation(-4)
        };
    }

    private static SessionResponse CreatedState(IActionResult result)
    {
        var created = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status201Created, created.StatusCode);
        return Assert.IsType<SessionResponse>(created.Value);
    }

    private static SessionResponse MoveState(IActionResult result)
    {
        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.Equal(StatusCodes.Status200OK, ok.StatusCode);
        return Assert.IsType<SessionResponse>(ok.Value);
    }

    private static void AssertState(SessionResponse response, int number, int moves, params int[] cardIds)
    {
        Assert.Equal(number, response.CurrentNumber);
        Assert.Equal(moves, response.MovesUsed);
        Assert.Equal(cardIds, response.AvailableOperations.Select(card => card.Id));
        // Rejected and overlapping requests must also leave exactly one entry per accepted move.
        Assert.Equal(moves, response.History.Count);
    }

    private static void AssertHistory(SessionResponse response,
        params (int BeforeNumber, string OperationDescription, int AfterNumber)[] expected)
    {
        Assert.Equal(expected, response.History.Select(move =>
            (move.BeforeNumber, move.OperationDescription, move.AfterNumber)));
    }

    private static void AssertProblem(IActionResult result, int statusCode)
    {
        var error = Assert.IsType<ObjectResult>(result);
        Assert.Equal(statusCode, error.StatusCode);
        var problem = Assert.IsType<ProblemDetails>(error.Value);
        Assert.Equal(statusCode, problem.Status);
        Assert.False(string.IsNullOrWhiteSpace(problem.Detail));
    }

    private static Task<IActionResult> OnThread(Func<IActionResult> action)
    {
        return Task.Factory.StartNew(action, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
    }

    private sealed class PausingOperation : IMathOperation, IDisposable
    {
        private int _applyCalls;
        public ManualResetEventSlim Entered { get; } = new ManualResetEventSlim(false);
        public ManualResetEventSlim Release { get; } = new ManualResetEventSlim(false);
        public int ApplyCalls => Volatile.Read(ref _applyCalls);
        public string Description => "Add 5";

        public int Apply(int input)
        {
            if (Interlocked.Increment(ref _applyCalls) == 1)
            {
                Entered.Set();
                if (!Release.Wait(TimeSpan.FromSeconds(10)))
                {
                    throw new TimeoutException("The test did not release the paused operation.");
                }
            }

            return input + 5;
        }

        public void Dispose()
        {
            Entered.Dispose();
            Release.Dispose();
        }
    }
}
