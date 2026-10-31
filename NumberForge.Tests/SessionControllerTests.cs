using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using NumberForge.Api.Contracts;
using NumberForge.Api.Controllers;
using NumberForge.Api.Persistence;
using NumberForge.Api.Services;
using NumberForge.Core;

namespace NumberForge.Tests;

public class SessionControllerTests
{
    [Theory]
    [InlineData(1, 2, 7, 3, "Add 2 (Echo ×2)", "Multiply by 2")]
    [InlineData(2, 3, 6, 1, "Multiply by 2", "Add 4 (Echo ×2)")]
    public async Task ApplyMove_WithEitherDemoRoute_ReachesTargetAndPreservesResponseSnapshots(
        int firstCardId, int secondCardId, int intermediateNumber, int unusedCardId,
        string firstDescription, string secondDescription)
    {
        var store = new GameSessionStore();
        var controller = new SessionController(store, new PuzzleCatalog()).ForPlayer();
        var created = CreatedState(await controller.Create());

        // The preview and newly created game must agree on numbers, budget, and card IDs.
        var previewResult = Assert.IsType<OkObjectResult>(await new PuzzleController(new PuzzleCatalog(), new PuzzleResponseMapper(new GameSessionStore())).ForPlayer().GetById("demo"));
        var preview = Assert.IsType<PuzzleResponse>(previewResult.Value);
        Assert.Equal(3, created.CurrentNumber);
        Assert.Equal(14, created.TargetNumber);
        Assert.Equal(2, created.MaxMoves);
        Assert.Equal(new[] { "Add 2", "Multiply by 2", "Add 4" },
            created.AvailableOperations.Select(card => card.Description));
        Assert.Equal(created.CurrentNumber, preview.StartNumber);
        Assert.Equal(created.TargetNumber, preview.TargetNumber);
        Assert.Equal(created.MaxMoves, preview.MaxMoves);
        Assert.Equal(created.AvailableOperations.Select(card => (card.Id, card.Description)),
            preview.Operations.Select(card => (card.Id, card.Description)));
        Assert.Equal("Echo", Assert.IsType<CharmResponse>(created.Charm).Name);
        Assert.False(created.Charm.IsUsed);

        // Predicted results stay hidden both before and during play, so planning remains the player's task.
        Assert.All(preview.Operations, card => Assert.Null(card.PreviewNumber));
        Assert.All(created.AvailableOperations, card => Assert.Null(card.PreviewNumber));

        // IDs stay attached to the original cards as the available hand shrinks.
        var remainingAfterFirstMove = new[] { 1, 2, 3 }.Where(id => id != firstCardId).ToArray();
        var firstMove = MoveState(await controller.ApplyMove(created.SessionId, new ApplyMoveRequest(firstCardId)));
        AssertState(firstMove, intermediateNumber, 1, remainingAfterFirstMove);
        Assert.All(firstMove.AvailableOperations, card => Assert.Null(card.PreviewNumber));

        var finalMove = MoveState(await controller.ApplyMove(created.SessionId, new ApplyMoveRequest(secondCardId)));
        AssertState(finalMove, 14, 2, unusedCardId);
        Assert.All(finalMove.AvailableOperations, card => Assert.Null(card.PreviewNumber));
        Assert.Equal(finalMove.TargetNumber, finalMove.CurrentNumber);
        var retrieved = MoveState(await controller.GetById(created.SessionId));
        AssertState(retrieved, 14, 2, unusedCardId);
        Assert.All(retrieved.AvailableOperations, card => Assert.Null(card.PreviewNumber));
        Assert.True(Assert.IsType<CharmResponse>(finalMove.Charm).IsUsed);
        Assert.True(Assert.IsType<CharmResponse>(retrieved.Charm).IsUsed);

        // Later moves must not change DTOs that were already returned to a client.
        AssertState(created, 3, 0, 1, 2, 3);
        AssertState(firstMove, intermediateNumber, 1, remainingAfterFirstMove);
        Assert.False(created.Charm.IsUsed);
        Assert.Equal(firstCardId == 1, Assert.IsType<CharmResponse>(firstMove.Charm).IsUsed);
        Assert.All(created.AvailableOperations, card => Assert.Null(card.PreviewNumber));

        // History is mapped in order, and earlier responses retain only the moves they captured.
        AssertHistory(created);
        AssertHistory(firstMove, (3, firstDescription, intermediateNumber));
        AssertHistory(finalMove, (3, firstDescription, intermediateNumber), (intermediateNumber, secondDescription, 14));
        AssertHistory(retrieved, (3, firstDescription, intermediateNumber), (intermediateNumber, secondDescription, 14));
    }

    [Fact]
    public async Task GetByIdAndApplyMove_WithEcho_ReportCharmStateAndPreserveEarlierSnapshots()
    {
        var store = new GameSessionStore();
        var controller = new SessionController(store, new PuzzleCatalog()).ForPlayer();
        var session = new GameSession(new Puzzle(3, 14), 2,
            new IMathOperation[] { new MathOperation(2), new MultiplyOperation(2), new MathOperation(4) },
            CharmType.Echo);
        Guid id = (await store.AddAsync(session, TestPlayer.Id))!.Value;

        // Reading a new Echo game supplies the rule and its unused state.
        var beforeMove = MoveState(await controller.GetById(id));
        var readyCharm = Assert.IsType<CharmResponse>(beforeMove.Charm);
        Assert.Equal("Echo", readyCharm.Name);
        Assert.Equal("Your first positive addition activates twice.", readyCharm.Description);
        Assert.False(readyCharm.IsUsed);
        Assert.All(beforeMove.AvailableOperations, card => Assert.Null(card.PreviewNumber));

        var afterMove = MoveState(await controller.ApplyMove(id, new ApplyMoveRequest(1)));

        AssertState(afterMove, 7, 1, 2, 3);
        Assert.True(Assert.IsType<CharmResponse>(afterMove.Charm).IsUsed);
        AssertHistory(afterMove, (3, "Add 2 (Echo ×2)", 7));
        Assert.All(afterMove.AvailableOperations, card => Assert.Null(card.PreviewNumber));

        // A later GET includes the spent charm, while the earlier DTO stays unused.
        var retrieved = MoveState(await controller.GetById(id));
        Assert.True(Assert.IsType<CharmResponse>(retrieved.Charm).IsUsed);
        AssertState(retrieved, 7, 1, 2, 3);
        Assert.All(retrieved.AvailableOperations, card => Assert.Null(card.PreviewNumber));
        Assert.False(readyCharm.IsUsed);
        AssertState(beforeMove, 3, 0, 1, 2, 3);
        Assert.All(beforeMove.AvailableOperations, card => Assert.Null(card.PreviewNumber));

        var finalMove = MoveState(await controller.ApplyMove(id, new ApplyMoveRequest(2)));
        AssertState(finalMove, 14, 2, 3);
        Assert.True(Assert.IsType<CharmResponse>(finalMove.Charm).IsUsed);
        Assert.False(readyCharm.IsUsed);
    }

    [Fact]
    public async Task ApplyMove_WhenSessionDoesNotExist_ReturnsNotFound()
    {
        var controller = new SessionController(new GameSessionStore(), new PuzzleCatalog()).ForPlayer();

        var result = await controller.ApplyMove(Guid.Empty, new ApplyMoveRequest(1));

        Assert.IsType<NotFoundResult>(result);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(4)]
    [InlineData(int.MaxValue)]
    public async Task ApplyMove_WhenCardIdIsInvalid_RejectsWithoutChangingState(int operationId)
    {
        var controller = new SessionController(new GameSessionStore(), new PuzzleCatalog()).ForPlayer();
        var created = CreatedState(await controller.Create());

        var result = await controller.ApplyMove(created.SessionId, new ApplyMoveRequest(operationId));

        AssertProblem(result, StatusCodes.Status400BadRequest);
        AssertState(MoveState(await controller.GetById(created.SessionId)), 3, 0, 1, 2, 3);
    }

    [Fact]
    public async Task ApplyMove_WhenCardWasAlreadyUsed_ReturnsConflictWithoutChangingState()
    {
        var controller = new SessionController(new GameSessionStore(), new PuzzleCatalog()).ForPlayer();
        var created = CreatedState(await controller.Create());
        MoveState(await controller.ApplyMove(created.SessionId, new ApplyMoveRequest(1)));

        var result = await controller.ApplyMove(created.SessionId, new ApplyMoveRequest(1));

        AssertProblem(result, StatusCodes.Status409Conflict);
        AssertState(MoveState(await controller.GetById(created.SessionId)), 7, 1, 2, 3);
    }

    [Fact]
    public async Task ApplyMove_WhenTargetWasReached_RejectsAnUnusedCard()
    {
        var store = new GameSessionStore();
        var controller = new SessionController(store, new PuzzleCatalog()).ForPlayer();
        var game = new GameSession(new Puzzle(3, 8), 3, GuardTestOperations());
        Guid id = (await store.AddAsync(game, TestPlayer.Id))!.Value;
        Assert.Null(MoveState(await controller.GetById(id)).Charm);
        MoveState(await controller.ApplyMove(id, new ApplyMoveRequest(1)));

        // Card 2 is unused and moves remain, so Core must reject because the target was reached.
        var result = await controller.ApplyMove(id, new ApplyMoveRequest(2));

        AssertProblem(result, StatusCodes.Status409Conflict);
        var retrieved = MoveState(await controller.GetById(id));
        AssertState(retrieved, 8, 1, 2, 3);
        Assert.Null(retrieved.Charm);
        Assert.All(retrieved.AvailableOperations, card => Assert.Null(card.PreviewNumber));
    }

    [Fact]
    public async Task ApplyMove_WhenMoveLimitWasReached_RejectsAnUnusedCard()
    {
        var store = new GameSessionStore();
        var controller = new SessionController(store, new PuzzleCatalog()).ForPlayer();
        var game = new GameSession(new Puzzle(3, 12), 1, GuardTestOperations());
        Guid id = (await store.AddAsync(game, TestPlayer.Id))!.Value;
        MoveState(await controller.ApplyMove(id, new ApplyMoveRequest(1)));

        // The target is not reached and card 2 remains, isolating the move-limit rule.
        var result = await controller.ApplyMove(id, new ApplyMoveRequest(2));

        AssertProblem(result, StatusCodes.Status409Conflict);
        var retrieved = MoveState(await controller.GetById(id));
        AssertState(retrieved, 8, 1, 2, 3);
        Assert.All(retrieved.AvailableOperations, card => Assert.Null(card.PreviewNumber));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ApplyMove_WithOverlappingApiInstances_CommitsOnceAndKeepsReadsConsistent(bool sameRequestId)
    {
        var repository = new PausingRepository();
        var limits = Options.Create(new SessionLimits());
        var firstStore = new GameSessionStore(TimeProvider.System, repository, limits);
        var secondStore = new GameSessionStore(TimeProvider.System, repository, limits);
        var firstController = new SessionController(firstStore, new PuzzleCatalog()).ForPlayer();
        var secondController = new SessionController(secondStore, new PuzzleCatalog()).ForPlayer();
        var independentController = new SessionController(
            new GameSessionStore(TimeProvider.System, repository, limits), new PuzzleCatalog()).ForPlayer();
        Guid id = (await firstStore.AddAsync(new GameSession(new Puzzle(3, 99), 2,
            new IMathOperation[] { new MathOperation(5) }), TestPlayer.Id))!.Value;
        var otherGame = CreatedState(await firstController.Create());
        repository.PausedSession = id;

        // Two API instances have separate locks. Pause both before saving the same old revision,
        // so the repository must resolve the race instead of relying on an in-process lock.
        Guid? requestId = sameRequestId ? Guid.NewGuid() : null;
        var first = firstController.ApplyMove(id, new ApplyMoveRequest(1, requestId: requestId));
        var duplicate = secondController.ApplyMove(id, new ApplyMoveRequest(1, requestId: requestId));
        try
        {
            await repository.BothEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));

            // During the pending write a reader sees the complete previous state, never half a move.
            AssertState(MoveState(await firstController.GetById(id)), 3, 0, 1);
            AssertState(MoveState(await independentController.ApplyMove(otherGame.SessionId, new ApplyMoveRequest(1))), 7, 1, 2, 3);
            repository.Release.TrySetResult();

            var results = await Task.WhenAll(first, duplicate).WaitAsync(TimeSpan.FromSeconds(5));
            var accepted = MoveState(results.OfType<OkObjectResult>().First());
            AssertState(accepted, 8, 1);
            if (sameRequestId)
                Assert.All(results, result => AssertState(MoveState(result), 8, 1));
            else
            {
                Assert.Single(results.OfType<OkObjectResult>());
                AssertProblem(Assert.Single(results, result => result is not OkObjectResult), StatusCodes.Status409Conflict);
            }
            var restored = MoveState(await secondController.GetById(id));
            AssertState(restored, 8, 1);
            Assert.NotNull(accepted.ElapsedSeconds);
            Assert.Equal(accepted.ElapsedSeconds, restored.ElapsedSeconds);
        }
        finally
        {
            repository.Release.TrySetResult();
            await Task.WhenAll(first, duplicate).WaitAsync(TimeSpan.FromSeconds(5));
        }
    }

    private static IMathOperation[] GuardTestOperations()
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
        var created = Assert.IsType<CreatedAtActionResult>(result);
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

    private sealed class PausingRepository : IGameStateRepository
    {
        private readonly InMemoryGameStateRepository _inner = new();
        private int _pending;
        public Guid PausedSession { get; set; }
        public TaskCompletionSource BothEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<SessionDocument?> FindAsync(Guid id, string playerId, DateTimeOffset now, CancellationToken cancellationToken = default) =>
            _inner.FindAsync(id, playerId, now, cancellationToken);
        public Task<bool> TryCreateAsync(SessionDocument document, int perPlayer, int total, DateTimeOffset now, CancellationToken cancellationToken = default) =>
            _inner.TryCreateAsync(document, perPlayer, total, now, cancellationToken);
        public async Task<bool> TrySaveAsync(SessionDocument document, int expectedRevision, RouteDocument? route, DateTimeOffset now,
            CancellationToken cancellationToken = default)
        {
            if (document.Id == PausedSession)
            {
                if (Interlocked.Increment(ref _pending) == 2) BothEntered.TrySetResult();
                await Release.Task.WaitAsync(TimeSpan.FromSeconds(10), cancellationToken);
            }
            return await _inner.TrySaveAsync(document, expectedRevision, route, now, cancellationToken);
        }
        public Task<IReadOnlyList<RouteDocument>> GetRoutesAsync(string playerId, string key, DateTimeOffset now,
            CancellationToken cancellationToken = default) => _inner.GetRoutesAsync(playerId, key, now, cancellationToken);
        public Task CleanupAsync(DateTimeOffset now, CancellationToken cancellationToken = default) => _inner.CleanupAsync(now, cancellationToken);
    }

}
