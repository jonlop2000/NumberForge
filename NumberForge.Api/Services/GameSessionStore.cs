using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.Extensions.Options;
using NumberForge.Api.Persistence;
using NumberForge.Core;

namespace NumberForge.Api.Services;

public sealed class SessionLimits
{
    public int MaxSessionsPerPlayer { get; set; } = 200;
    public int MaxSessionsTotal { get; set; } = 50_000;
}

public enum MoveStatus { Accepted, NotFound, Expired, InvalidOperation, Conflict }
public sealed record MoveResult(MoveStatus Status, SessionState? State = null);
public sealed record SessionState(SessionDocument Document, GameSession Game, double? ElapsedSeconds);

// Core owns the game rules. This service owns identity, lifetime, persistence, and accepted requests.
public sealed class GameSessionStore
{
    private readonly IGameStateRepository _repository;
    private readonly TimeProvider _clock;
    private readonly SessionLimits _limits;
    // Fixed lock stripes serialize requests without an ever-growing dictionary of lock objects.
    private readonly SemaphoreSlim[] _locks = Enumerable.Range(0, 256).Select(_ => new SemaphoreSlim(1, 1)).ToArray();
    private readonly ConcurrentDictionary<Guid, (long Timestamp, DateTimeOffset Expires)> _timers = new();
    private long _nextTimerCleanup;

    public GameSessionStore() : this(TimeProvider.System) { }
    public GameSessionStore(TimeProvider clock) : this(clock, new InMemoryGameStateRepository(), Options.Create(new SessionLimits())) { }
    public GameSessionStore(TimeProvider clock, IGameStateRepository repository, IOptions<SessionLimits> limits)
    {
        _clock = clock;
        _repository = repository;
        _limits = limits.Value;
    }

    public async Task<Guid?> AddAsync(GameSession game, string playerId, DateTimeOffset? playUntilUtc = null, CancellationToken cancellationToken = default)
    {
        var now = _clock.GetUtcNow();
        if (playUntilUtc <= now) return null;
        // Keep yesterday's completed result readable for a day, while disallowing further daily moves.
        var expires = playUntilUtc?.AddDays(1) ?? now.AddDays(1);
        var id = Guid.NewGuid();
        var payload = GamePayload.Capture(game, game.IsFinished ? 0 : null, []);
        var document = new SessionDocument(id, playerId, now, expires, playUntilUtc, 0, JsonSerializer.Serialize(payload));
        if (!await _repository.TryCreateAsync(document, _limits.MaxSessionsPerPlayer, _limits.MaxSessionsTotal, now, cancellationToken)) return null;
        // Retain monotonic timing within this process. Recovered sessions use their persisted UTC start.
        // Scan at most once a minute instead of scanning every saved attempt on each new game.
        long cleanup = Interlocked.Read(ref _nextTimerCleanup);
        if (now.UtcTicks >= cleanup && Interlocked.CompareExchange(ref _nextTimerCleanup, now.AddMinutes(1).UtcTicks, cleanup) == cleanup)
            foreach (var expired in _timers.Where(p => p.Value.Expires <= now)) _timers.TryRemove(expired.Key, out _);
        if (_timers.Count < _limits.MaxSessionsTotal) _timers[id] = (_clock.GetTimestamp(), expires);
        return id;
    }

    public async Task<SessionState?> GetAsync(Guid id, string playerId, CancellationToken cancellationToken = default)
    {
        var document = await _repository.FindAsync(id, playerId, _clock.GetUtcNow(), cancellationToken);
        return document is null ? null : Restore(document);
    }

    public async Task<MoveResult> ApplyAsync(Guid id, string playerId, int operationId, int? expectedRevision = null,
        Guid? requestId = null, CancellationToken cancellationToken = default)
    {
        var gate = _locks[(int)((uint)id.GetHashCode() % (uint)_locks.Length)];
        await gate.WaitAsync(cancellationToken);
        try
        {
            var now = _clock.GetUtcNow();
            var document = await _repository.FindAsync(id, playerId, now, cancellationToken);
            if (document is null) return new(MoveStatus.NotFound);
            var payload = Deserialize(document);
            var game = payload.RestoreGame();
            // Repeated delivery of an accepted request returns state without spending a second card.
            var receipt = requestId is null ? null : payload.Requests.FirstOrDefault(r => r.RequestId == requestId);
            if (receipt is not null)
                return receipt.OperationId == operationId ? new(MoveStatus.Accepted, new(document, game, payload.ElapsedSeconds)) : new(MoveStatus.Conflict);
            if (document.PlayUntilUtc <= now) return new(MoveStatus.Expired);
            if (expectedRevision is not null && expectedRevision != document.Revision) return new(MoveStatus.Conflict);
            if (operationId < 1 || operationId > game.StartingOperations.Count) return new(MoveStatus.InvalidOperation);
            if (!game.TryApplyOperation(game.StartingOperations[operationId - 1])) return new(MoveStatus.Conflict);

            double? elapsed = payload.ElapsedSeconds;
            if (game.IsFinished && elapsed is null)
            {
                elapsed = _timers.TryGetValue(id, out var timer)
                    ? _clock.GetElapsedTime(timer.Timestamp).TotalSeconds
                    : (now - document.StartedAtUtc).TotalSeconds;
                elapsed = Math.Max(0, elapsed.Value);
            }
            var receipts = payload.Requests.ToList();
            if (requestId is Guid request) receipts.Add(new(request, operationId));
            var next = document with { Revision = document.Revision + 1, Payload = JsonSerializer.Serialize(GamePayload.Capture(game, elapsed, receipts.ToArray())) };
            RouteDocument? route = game.Definition is not null && game.Puzzle.IsTargetReached(game.CurrentNumber)
                ? new(playerId, RouteIdentity.GetCollectionKey(game.Definition), RouteIdentity.GetWinningRouteId(game)!,
                    game.MovesUsed, now, document.PlayUntilUtc?.AddDays(1) ?? now.AddDays(30)) : null;
            // The repository checks the version again: another API instance may have committed first.
            if (!await _repository.TrySaveAsync(next, document.Revision, route, now, cancellationToken))
            {
                // Two API instances can receive the same retry at once. The losing writer checks
                // whether the winning writer already committed this exact request before reporting a conflict.
                if (requestId is not null)
                {
                    var latest = await _repository.FindAsync(id, playerId, _clock.GetUtcNow(), cancellationToken);
                    if (latest is not null)
                    {
                        var saved = Deserialize(latest);
                        if (saved.Requests.Any(r => r.RequestId == requestId && r.OperationId == operationId))
                            return new(MoveStatus.Accepted, new(latest, saved.RestoreGame(), saved.ElapsedSeconds));
                    }
                }
                return new(MoveStatus.Conflict);
            }
            return new(MoveStatus.Accepted, new(next, game, elapsed));
        }
        finally { gate.Release(); }
    }

    public Task<IReadOnlyList<RouteDocument>> GetRoutesAsync(string playerId, string collectionKey, CancellationToken cancellationToken = default) =>
        _repository.GetRoutesAsync(playerId, collectionKey, _clock.GetUtcNow(), cancellationToken);

    private static GamePayload Deserialize(SessionDocument document) => JsonSerializer.Deserialize<GamePayload>(document.Payload)
        ?? throw new InvalidOperationException("Stored session is invalid.");
    private static SessionState Restore(SessionDocument document)
    {
        var payload = Deserialize(document);
        return new(document, payload.RestoreGame(), payload.ElapsedSeconds);
    }

    // Explicit operation kinds are an allowlist. Never deserialize arbitrary CLR types from storage.
    public sealed record CardState(string Kind, int Value)
    {
        public static CardState From(IMathOperation operation) => operation switch
        {
            MathOperation add => new("add", add.Amount),
            MultiplyOperation multiply => new("multiply", multiply.Factor),
            _ => throw new InvalidOperationException("Only supported arithmetic cards can be persisted.")
        };
        public IMathOperation Restore() => Kind switch
        {
            "add" => new MathOperation(Value), "multiply" => new MultiplyOperation(Value),
            _ => throw new InvalidOperationException("Unknown stored card kind.")
        };
    }
    public sealed record RequestReceipt(Guid RequestId, int OperationId);
    public sealed record GamePayload(string? PuzzleId, string? PuzzleName, int Start, int Target, int MaxMoves,
        int? MinimumMoves, int? TotalRoutes, CharmType Charm, CardState[] Cards, int[] Moves,
        double? ElapsedSeconds, RequestReceipt[] Requests)
    {
        public static GamePayload Capture(GameSession game, double? elapsed, RequestReceipt[] requests) => new(
            game.Definition?.Id, game.Definition?.Name, game.Puzzle.StartNumber, game.Puzzle.TargetNumber, game.MaxMoves,
            game.Definition?.MinimumMoves, game.Definition?.TotalRoutes, game.Charm,
            game.StartingOperations.Select(CardState.From).ToArray(), game.History.Select(m => m.OperationId).ToArray(), elapsed, requests);
        public GameSession RestoreGame()
        {
            var cards = Cards.Select(c => c.Restore()).ToArray();
            var puzzle = new Puzzle(Start, Target);
            var game = PuzzleId is not null
                ? new GameSession(new PuzzleDefinition(PuzzleId, PuzzleName!, puzzle, MaxMoves, MinimumMoves!.Value, TotalRoutes!.Value, cards, Charm))
                : new GameSession(puzzle, MaxMoves, cards, Charm);
            foreach (int id in Moves)
                if (id < 1 || id > cards.Length || !game.TryApplyOperation(cards[id - 1])) throw new InvalidOperationException("Stored move history is invalid.");
            return game;
        }
    }
}
