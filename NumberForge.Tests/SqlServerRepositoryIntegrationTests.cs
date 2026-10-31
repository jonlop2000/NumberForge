using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NumberForge.Api.Persistence;

namespace NumberForge.Tests;

// These tests exercise real SQL Server transaction/locking behavior. EF's memory provider or
// SQLite would give false confidence here because their SQL and concurrency rules differ.
[Collection("SQL Server persistence")]
public sealed class SqlServerRepositoryIntegrationTests
{
    [SqlServerFact]
    public async Task SharedDatabase_EnforcesPlayerAndGlobalQuotasAcrossRepositoryInstances()
    {
        await using var fixture = await DatabaseFixture.CreateAsync();
        string player = Guid.NewGuid().ToString("N");
        var now = DateTimeOffset.UtcNow;
        var candidates = Enumerable.Range(0, 24).Select(_ => NewSession(player, now)).ToArray();
        try
        {
            var accepted = await Task.WhenAll(candidates.Select((session, index) =>
                fixture.Repository(index % 2).TryCreateAsync(session, 2, 100_000, now)));
            Assert.Equal(2, accepted.Count(success => success));
            await using var database = fixture.CreateContext();
            Assert.Equal(2, await database.Sessions.CountAsync(session => session.PlayerId == player));

            int activeCount = await database.Sessions.CountAsync(session => session.ExpiresAtUtc > now);
            var globallyLimited = await Task.WhenAll(Enumerable.Range(0, 12).Select(index =>
                fixture.Repository(index % 2).TryCreateAsync(NewSession(player, now), 100, activeCount + 1, now)));
            Assert.Single(globallyLimited, success => success);
        }
        finally
        {
            await fixture.RemovePlayerAsync(player);
        }
    }

    [SqlServerFact]
    public async Task WinningMove_IsAtomicOwnerScopedAndIdempotentAcrossIndependentRepositories()
    {
        await using var fixture = await DatabaseFixture.CreateAsync();
        string player = Guid.NewGuid().ToString("N");
        var now = DateTimeOffset.UtcNow;
        var first = NewSession(player, now);
        var second = NewSession(player, now);
        var route = new RouteDocument(player, Guid.NewGuid().ToString("N"), Guid.NewGuid().ToString("N"),
            2, now, now.AddMinutes(20));
        try
        {
            Assert.True(await fixture.Repository(0).TryCreateAsync(first, 10, 100_000, now));
            Assert.True(await fixture.Repository(1).TryCreateAsync(second, 10, 100_000, now));
            Assert.Null(await fixture.Repository(1).FindAsync(first.Id, "another-player", now));
            Assert.Equal(first, await fixture.Repository(1).FindAsync(first.Id, player, now));

            // Both requests read revision zero, but exactly one is allowed to commit it.
            var wins = await Task.WhenAll(Enumerable.Range(0, 12).Select(index =>
                fixture.Repository(index % 2).TrySaveAsync(first with { Revision = 1, Payload = "{\"won\":true}" },
                    0, route, now)));
            Assert.Single(wins, success => success);
            Assert.Equal(1, (await fixture.Repository(1).FindAsync(first.Id, player, now))!.Revision);

            // A second successful attempt on the same route must not count as a second discovery.
            Assert.True(await fixture.Repository(1).TrySaveAsync(second with { Revision = 1 }, 0, route, now));
            Assert.Single(await fixture.Repository(0).GetRoutesAsync(player, route.CollectionKey, now));
            Assert.Empty(await fixture.Repository(0).GetRoutesAsync("another-player", route.CollectionKey, now));

            // A failed CAS must also leave the discovery table unchanged.
            var newRoute = route with { RouteId = Guid.NewGuid().ToString("N") };
            Assert.False(await fixture.Repository(0).TrySaveAsync(first with { Revision = 1 }, 0, newRoute, now));
            Assert.Single(await fixture.Repository(1).GetRoutesAsync(player, route.CollectionKey, now));
            Assert.False(await fixture.Repository(0).TrySaveAsync(first with
            {
                Revision = 2, StartedAtUtc = first.StartedAtUtc.AddSeconds(-1)
            }, 1, newRoute, now));
            Assert.False(await fixture.Repository(0).TrySaveAsync(first with { Revision = 2 }, 1, null,
                first.PlayUntilUtc!.Value));
            Assert.Null(await fixture.Repository(0).FindAsync(first.Id, player, first.ExpiresAtUtc));
            Assert.Empty(await fixture.Repository(0).GetRoutesAsync(player, route.CollectionKey, route.ExpiresAtUtc));
        }
        finally
        {
            await fixture.RemovePlayerAsync(player);
        }
    }

    [SqlServerFact]
    public async Task Cleanup_DeletesAtMostOneBatchAndNeverDeletesLiveRows()
    {
        await using var fixture = await DatabaseFixture.CreateAsync();
        string player = Guid.NewGuid().ToString("N");
        var now = DateTimeOffset.UtcNow;
        try
        {
            // Use a far-past cutoff so this check cannot remove other tests' recently expired rows.
            var cutoff = now.AddYears(-20);
            await using (var database = fixture.CreateContext())
            {
                for (int index = 0; index < SqlServerGameStateRepository.CleanupBatchSize + 5; index++)
                {
                    database.Sessions.Add(new GameSessionEntity
                    {
                        Id = Guid.NewGuid(), PlayerId = player, StartedAtUtc = cutoff.AddDays(-2),
                        ExpiresAtUtc = cutoff.AddDays(-1), Revision = 0, Payload = "{}"
                    });
                    database.Routes.Add(new DiscoveredRouteEntity
                    {
                        PlayerId = player, CollectionKey = "expired", RouteId = index.ToString(),
                        MoveCount = 1, FoundAtUtc = cutoff.AddDays(-2), ExpiresAtUtc = cutoff.AddDays(-1)
                    });
                }
                await database.SaveChangesAsync();
            }
            var live = NewSession(player, now);
            Assert.True(await fixture.Repository(0).TryCreateAsync(live, 10, 100_000, now));
            await fixture.Repository(0).CleanupAsync(cutoff);
            await using var remaining = fixture.CreateContext();
            Assert.Equal(5, await remaining.Sessions.CountAsync(session => session.PlayerId == player && session.ExpiresAtUtc <= cutoff));
            Assert.Equal(5, await remaining.Routes.CountAsync(route => route.PlayerId == player));
            Assert.NotNull(await fixture.Repository(1).FindAsync(live.Id, player, now));
        }
        finally
        {
            await fixture.RemovePlayerAsync(player);
        }
    }

    private static SessionDocument NewSession(string player, DateTimeOffset now)
        => new(Guid.NewGuid(), player, now, now.AddHours(1), now.AddMinutes(30), 0, "{}");

    private sealed class DatabaseFixture(ServiceProvider first, ServiceProvider second) : IAsyncDisposable
    {
        public static async Task<DatabaseFixture> CreateAsync()
        {
            string connection = Environment.GetEnvironmentVariable(SqlServerFactAttribute.ConnectionVariable)!;
            // An explicitly named test database is required. The test suite does not provision databases.
            if (!new SqlConnectionStringBuilder(connection).InitialCatalog.EndsWith("_tests", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("SQL integration tests require a dedicated database whose name ends in _tests.");
            }
            ServiceProvider BuildProvider() => new ServiceCollection().AddLogging()
                .AddSqlServerGameState(connection).BuildServiceProvider();
            var fixture = new DatabaseFixture(BuildProvider(), BuildProvider());
            try
            {
                await using var database = fixture.CreateContext();
                string scriptPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
                    "../../../../NumberForge.Api/Persistence/Schema/001_game_state.sql"));
                string script = await File.ReadAllTextAsync(scriptPath);
                // Running twice verifies the checked-in initial schema script is idempotent on SQL Server.
                await database.Database.ExecuteSqlRawAsync(script);
                await database.Database.ExecuteSqlRawAsync(script);
                return fixture;
            }
            catch
            {
                await fixture.DisposeAsync();
                throw;
            }
        }

        public IGameStateRepository Repository(int index)
            => (index == 0 ? first : second).GetRequiredService<IGameStateRepository>();

        public GameStateDbContext CreateContext()
            => first.GetRequiredService<IDbContextFactory<GameStateDbContext>>().CreateDbContext();

        public async Task RemovePlayerAsync(string player)
        {
            await using var database = CreateContext();
            await database.Sessions.Where(session => session.PlayerId == player).ExecuteDeleteAsync();
            await database.Routes.Where(route => route.PlayerId == player).ExecuteDeleteAsync();
        }

        public async ValueTask DisposeAsync()
        {
            await first.DisposeAsync();
            await second.DisposeAsync();
        }
    }
}

[CollectionDefinition("SQL Server persistence", DisableParallelization = true)]
public sealed class SqlServerPersistenceCollection;

public sealed class SqlServerFactAttribute : FactAttribute
{
    public const string ConnectionVariable = "NUMBERFORGE_TEST_SQLSERVER";

    public SqlServerFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(ConnectionVariable)))
        {
            Skip = "Set NUMBERFORGE_TEST_SQLSERVER to a dedicated SQL Server database ending in _tests.";
        }
    }
}
