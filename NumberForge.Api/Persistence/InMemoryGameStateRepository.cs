namespace NumberForge.Api.Persistence;

// Development/test adapter. Production selects SQL Server so successful moves survive restarts.
public sealed class InMemoryGameStateRepository : IGameStateRepository
{
    private readonly object _gate = new();
    private readonly Dictionary<Guid, SessionDocument> _sessions = new();
    private readonly Dictionary<(string Player, string Collection, string Route), RouteDocument> _routes = new();

    public Task<SessionDocument?> FindAsync(Guid id, string playerId, DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
            return Task.FromResult(_sessions.TryGetValue(id, out var s) && s.PlayerId == playerId && s.ExpiresAtUtc > now ? s : null);
    }

    public Task<bool> TryCreateAsync(SessionDocument session, int maxPerPlayer, int maxTotal, DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            Prune(now);
            if (session.Revision != 0 || session.StartedAtUtc > now || session.ExpiresAtUtc <= now || session.PlayUntilUtc <= now)
                return Task.FromResult(false);
            if (_sessions.Count >= maxTotal || _sessions.Values.Count(s => s.PlayerId == session.PlayerId) >= maxPerPlayer)
                return Task.FromResult(false);
            return Task.FromResult(_sessions.TryAdd(session.Id, session));
        }
    }

    public Task<bool> TrySaveAsync(SessionDocument session, int expectedRevision, RouteDocument? route, DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            if (!_sessions.TryGetValue(session.Id, out var current) || current.PlayerId != session.PlayerId ||
                current.Revision != expectedRevision || session.Revision != expectedRevision + 1 ||
                current.ExpiresAtUtc <= now || current.PlayUntilUtc <= now ||
                current.StartedAtUtc != session.StartedAtUtc || current.ExpiresAtUtc != session.ExpiresAtUtc || current.PlayUntilUtc != session.PlayUntilUtc)
                return Task.FromResult(false);
            if (route is not null && (route.PlayerId != session.PlayerId || route.FoundAtUtc > now || route.ExpiresAtUtc <= now))
                throw new ArgumentException("A discovery must belong to the active player and lifetime.", nameof(route));
            _sessions[session.Id] = session;
            if (route is not null)
            {
                var key = (route.PlayerId, route.CollectionKey, route.RouteId);
                if (!_routes.TryGetValue(key, out var existing) || existing.ExpiresAtUtc <= now) _routes[key] = route;
            }
            return Task.FromResult(true);
        }
    }

    public Task<IReadOnlyList<RouteDocument>> GetRoutesAsync(string playerId, string collectionKey, DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
            return Task.FromResult<IReadOnlyList<RouteDocument>>(_routes.Values
                .Where(r => r.PlayerId == playerId && r.CollectionKey == collectionKey && r.ExpiresAtUtc > now)
                .OrderBy(r => r.FoundAtUtc).ToArray());
    }

    public Task CleanupAsync(DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate) Prune(now);
        return Task.CompletedTask;
    }

    private void Prune(DateTimeOffset now)
    {
        foreach (var id in _sessions.Where(p => p.Value.ExpiresAtUtc <= now).Select(p => p.Key).ToArray()) _sessions.Remove(id);
        foreach (var id in _routes.Where(p => p.Value.ExpiresAtUtc <= now).Select(p => p.Key).ToArray()) _routes.Remove(id);
    }
}
