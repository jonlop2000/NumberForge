using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace NumberForge.Api.Persistence;

public sealed class SqlServerGameStateRepository(IDbContextFactory<GameStateDbContext> contexts)
    : IGameStateRepository
{
    public const int CleanupBatchSize = 500;
    private readonly IDbContextFactory<GameStateDbContext> _contexts = contexts
        ?? throw new ArgumentNullException(nameof(contexts));

    public async Task<SessionDocument?> FindAsync(Guid id, string playerId, DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        ValidateIdentifier(playerId, nameof(playerId));
        await using var database = await _contexts.CreateDbContextAsync(cancellationToken);
        // Knowing a session ID is insufficient: every lookup also checks its owner and lifetime.
        return await database.Sessions.AsNoTracking()
            .Where(session => session.Id == id && session.PlayerId == playerId && session.ExpiresAtUtc > now)
            .Select(session => new SessionDocument(session.Id, session.PlayerId, session.StartedAtUtc,
                session.ExpiresAtUtc, session.PlayUntilUtc, session.Revision, session.Payload))
            .SingleOrDefaultAsync(cancellationToken);
    }

    public async Task<bool> TryCreateAsync(SessionDocument session, int maxPerPlayer, int maxTotal,
        DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        ValidateSession(session);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxPerPlayer);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxTotal);
        if (session.Revision != 0 || session.StartedAtUtc > now || !CanPlay(session, now))
        {
            return false;
        }

        await using var database = await _contexts.CreateDbContextAsync(cancellationToken);
        await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken);
        // A transaction-owned SQL lock makes count + insert one admission decision even when
        // several API instances share the database. The lock is released at commit/rollback.
        if (!await TryAcquireLockAsync(database, "numberforge:session-admission", cancellationToken))
        {
            return false;
        }

        if (await database.Sessions.AnyAsync(existing => existing.Id == session.Id, cancellationToken)
            || await database.Sessions.CountAsync(existing => existing.ExpiresAtUtc > now, cancellationToken) >= maxTotal
            || await database.Sessions.CountAsync(existing => existing.PlayerId == session.PlayerId
                && existing.ExpiresAtUtc > now, cancellationToken) >= maxPerPlayer)
        {
            return false;
        }

        database.Sessions.Add(new GameSessionEntity
        {
            Id = session.Id, PlayerId = session.PlayerId, StartedAtUtc = session.StartedAtUtc,
            ExpiresAtUtc = session.ExpiresAtUtc, PlayUntilUtc = session.PlayUntilUtc,
            Revision = session.Revision, Payload = session.Payload
        });
        await database.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return true;
    }

    public async Task<bool> TrySaveAsync(SessionDocument session, int expectedRevision, RouteDocument? route,
        DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        ValidateSession(session);
        ArgumentOutOfRangeException.ThrowIfNegative(expectedRevision);
        if (expectedRevision == int.MaxValue || session.Revision != expectedRevision + 1 || !CanPlay(session, now))
        {
            return false;
        }
        if (route is not null)
        {
            ValidateRoute(route);
            if (route.PlayerId != session.PlayerId || route.FoundAtUtc > now || route.ExpiresAtUtc <= now)
            {
                throw new ArgumentException("A discovery must belong to the active session's player and lifetime.", nameof(route));
            }
        }

        await using var database = await _contexts.CreateDbContextAsync(cancellationToken);
        await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken);
        // Lock only this player's collection when a win needs a discovery. Other players and
        // ordinary moves can commit independently, without a process-wide gameplay lock.
        if (route is not null && !await TryAcquireLockAsync(database,
                $"numberforge:routes:{route.PlayerId}:{route.CollectionKey}", cancellationToken))
        {
            return false;
        }

        // The WHERE clause is the compare-and-swap. Only one request can consume a revision.
        // Identity, start time, and deadlines cannot be rewritten by a later move.
        int updated = await database.Sessions
            .Where(existing => existing.Id == session.Id && existing.PlayerId == session.PlayerId
                && existing.Revision == expectedRevision && existing.ExpiresAtUtc > now
                && (existing.PlayUntilUtc == null || existing.PlayUntilUtc > now)
                && existing.StartedAtUtc == session.StartedAtUtc && existing.ExpiresAtUtc == session.ExpiresAtUtc
                && existing.PlayUntilUtc == session.PlayUntilUtc)
            .ExecuteUpdateAsync(updates => updates
                .SetProperty(existing => existing.Revision, session.Revision)
                .SetProperty(existing => existing.Payload, session.Payload), cancellationToken);
        if (updated != 1)
        {
            return false;
        }

        if (route is not null)
        {
            var existing = await database.Routes.SingleOrDefaultAsync(discovery =>
                discovery.PlayerId == route.PlayerId && discovery.CollectionKey == route.CollectionKey
                && discovery.RouteId == route.RouteId, cancellationToken);
            if (existing is null)
            {
                database.Routes.Add(new DiscoveredRouteEntity
                {
                    PlayerId = route.PlayerId, CollectionKey = route.CollectionKey, RouteId = route.RouteId,
                    MoveCount = route.MoveCount, FoundAtUtc = route.FoundAtUtc, ExpiresAtUtc = route.ExpiresAtUtc
                });
            }
            else if (existing.ExpiresAtUtc <= now)
            {
                existing.MoveCount = route.MoveCount;
                existing.FoundAtUtc = route.FoundAtUtc;
                existing.ExpiresAtUtc = route.ExpiresAtUtc;
            }
            await database.SaveChangesAsync(cancellationToken);
        }

        // A winning state and its route become visible together, or both roll back.
        await transaction.CommitAsync(cancellationToken);
        return true;
    }

    public async Task<IReadOnlyList<RouteDocument>> GetRoutesAsync(string playerId, string collectionKey,
        DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        ValidateIdentifier(playerId, nameof(playerId));
        ValidateIdentifier(collectionKey, nameof(collectionKey));
        await using var database = await _contexts.CreateDbContextAsync(cancellationToken);
        var routes = await database.Routes.AsNoTracking()
            .Where(route => route.PlayerId == playerId && route.CollectionKey == collectionKey && route.ExpiresAtUtc > now)
            .OrderBy(route => route.FoundAtUtc).ThenBy(route => route.RouteId)
            .Select(route => new RouteDocument(route.PlayerId, route.CollectionKey, route.RouteId,
                route.MoveCount, route.FoundAtUtc, route.ExpiresAtUtc))
            .ToListAsync(cancellationToken);
        return routes.AsReadOnly();
    }

    public async Task CleanupAsync(DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        await using var database = await _contexts.CreateDbContextAsync(cancellationToken);
        // Fixed-size batches keep a cleanup tick from locking the entire history table.
        // ExecuteSqlInterpolated parameterizes the timestamp and batch size; neither is SQL text.
        await database.Database.ExecuteSqlInterpolatedAsync($"""
            DELETE TOP ({CleanupBatchSize}) FROM [numberforge].[Sessions] WHERE [ExpiresAtUtc] <= {now};
            DELETE TOP ({CleanupBatchSize}) FROM [numberforge].[Routes] WHERE [ExpiresAtUtc] <= {now};
            """, cancellationToken);
    }

    private static async Task<bool> TryAcquireLockAsync(GameStateDbContext database, string resource,
        CancellationToken cancellationToken)
    {
        await using var command = database.Database.GetDbConnection().CreateCommand();
        command.Transaction = database.Database.CurrentTransaction!.GetDbTransaction();
        command.CommandTimeout = 10;
        command.CommandText = """
            DECLARE @result int;
            EXEC @result = sys.sp_getapplock @Resource = @resource,
                @LockMode = 'Exclusive', @LockOwner = 'Transaction', @LockTimeout = 5000;
            SELECT @result;
            """;
        var parameter = command.CreateParameter();
        parameter.ParameterName = "@resource";
        parameter.DbType = DbType.String;
        parameter.Size = 255;
        parameter.Value = resource;
        command.Parameters.Add(parameter);
        // SQL Server returns a negative code on timeout/cancellation/deadlock. No writes have run yet.
        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken)) >= 0;
    }

    private static bool CanPlay(SessionDocument session, DateTimeOffset now)
        => session.ExpiresAtUtc > now && (session.PlayUntilUtc is null || session.PlayUntilUtc > now);

    private static void ValidateSession(SessionDocument session)
    {
        ArgumentNullException.ThrowIfNull(session);
        ValidateIdentifier(session.PlayerId, nameof(session.PlayerId));
        if (session.Id == Guid.Empty || session.Revision < 0 || session.ExpiresAtUtc <= session.StartedAtUtc
            || (session.PlayUntilUtc is { } playUntil
                && (playUntil <= session.StartedAtUtc || playUntil > session.ExpiresAtUtc))
            || string.IsNullOrWhiteSpace(session.Payload) || session.Payload.Length > GameStateDbContext.MaximumPayloadCharacters)
        {
            throw new ArgumentException("The session's identity, lifetime, revision, or payload is invalid.", nameof(session));
        }
    }

    private static void ValidateRoute(RouteDocument route)
    {
        ValidateIdentifier(route.PlayerId, nameof(route.PlayerId));
        ValidateIdentifier(route.CollectionKey, nameof(route.CollectionKey));
        ValidateIdentifier(route.RouteId, nameof(route.RouteId));
        if (route.MoveCount <= 0 || route.ExpiresAtUtc <= route.FoundAtUtc)
        {
            throw new ArgumentException("A route needs a positive move count and a valid lifetime.", nameof(route));
        }
    }

    private static void ValidateIdentifier(string identifier, string parameterName)
    {
        // Production identifiers are 64 hexadecimal characters. ASCII storage also makes
        // development identities unambiguous under the database's binary comparison rules.
        if (string.IsNullOrWhiteSpace(identifier) || identifier.Length > 64
            || identifier.Any(character => character < '!' || character > '~'))
        {
            throw new ArgumentException("An identifier must contain 1 to 64 printable ASCII characters.", parameterName);
        }
    }
}
