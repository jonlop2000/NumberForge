namespace NumberForge.Api.Persistence;

// The API persists only server-created state. Player requests never supply this payload.
public sealed record SessionDocument(Guid Id, string PlayerId, DateTimeOffset StartedAtUtc,
    DateTimeOffset ExpiresAtUtc, DateTimeOffset? PlayUntilUtc, int Revision, string Payload);

public sealed record RouteDocument(string PlayerId, string CollectionKey, string RouteId,
    int MoveCount, DateTimeOffset FoundAtUtc, DateTimeOffset ExpiresAtUtc);

public interface IGameStateRepository
{
    Task<SessionDocument?> FindAsync(Guid id, string playerId, DateTimeOffset now, CancellationToken cancellationToken = default);
    // Admission limits and insertion must be atomic, including across API instances.
    Task<bool> TryCreateAsync(SessionDocument session, int maxPerPlayer, int maxTotal,
        DateTimeOffset now, CancellationToken cancellationToken = default);
    // Compare-and-swap prevents two requests from overwriting each other's moves. A win and its
    // discovery are committed together; retrying the same route never adds a duplicate discovery.
    Task<bool> TrySaveAsync(SessionDocument session, int expectedRevision, RouteDocument? route,
        DateTimeOffset now, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<RouteDocument>> GetRoutesAsync(string playerId, string collectionKey,
        DateTimeOffset now, CancellationToken cancellationToken = default);
    Task CleanupAsync(DateTimeOffset now, CancellationToken cancellationToken = default);
}
