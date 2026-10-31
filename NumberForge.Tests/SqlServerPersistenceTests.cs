using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using NumberForge.Api.Persistence;
using System.Text.RegularExpressions;

namespace NumberForge.Tests;

public class SqlServerPersistenceTests
{
    private const string ModelConnection = "Server=sql.example.invalid;Database=NumberForge;Integrated Security=true;Encrypt=true";
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-10-30T12:00:00Z");

    [Fact]
    public void SqlModel_UsesOwnerScopedRouteKeysAndExpiryIndexes()
    {
        using var database = CreateContext();
        // The design-time model retains collation/check-constraint metadata used in schema scripts.
        var model = database.GetService<Microsoft.EntityFrameworkCore.Metadata.IDesignTimeModel>().Model;
        var sessions = model.FindEntityType(typeof(GameSessionEntity))!;
        var routes = model.FindEntityType(typeof(DiscoveredRouteEntity))!;

        Assert.Equal("numberforge", sessions.GetSchema());
        Assert.True(sessions.FindProperty(nameof(GameSessionEntity.Revision))!.IsConcurrencyToken);
        Assert.Contains(sessions.GetIndexes(), index => index.Properties.Select(property => property.Name)
            .SequenceEqual(new[] { "PlayerId", "ExpiresAtUtc" }));
        Assert.Contains(sessions.GetIndexes(), index => index.Properties.Count == 1 && index.Properties[0].Name == "ExpiresAtUtc");
        Assert.Equal(new[] { "PlayerId", "CollectionKey", "RouteId" },
            routes.FindPrimaryKey()!.Properties.Select(property => property.Name));
        foreach (var property in routes.FindPrimaryKey()!.Properties)
        {
            Assert.Equal(64, property.GetMaxLength());
            Assert.False(property.IsUnicode());
            Assert.Equal(GameStateDbContext.IdentifierCollation, property.GetCollation());
        }
        Assert.Contains(routes.GetIndexes(), index => index.Properties.Single().Name == "ExpiresAtUtc");
    }

    [Fact]
    public void GeneratedSqlSchema_EnforcesValidJsonAndBoundedPayloads()
    {
        using var database = CreateContext();
        string script = database.Database.GenerateCreateScript();
        Assert.Contains("CREATE TABLE [numberforge].[Sessions]", script);
        Assert.Contains("CREATE TABLE [numberforge].[Routes]", script);
        Assert.Contains("ISJSON([Payload]) = 1 AND DATALENGTH([Payload]) <= 131072", script);
        Assert.Contains("PRIMARY KEY ([PlayerId], [CollectionKey], [RouteId])", script);
        Assert.Contains("[PlayUntilUtc] > [StartedAtUtc] AND [PlayUntilUtc] <= [ExpiresAtUtc]", script);
        Assert.DoesNotContain("CASCADE", script);
    }

    [Fact]
    public void CheckedInSchema_MatchesTheEntityFrameworkModel()
    {
        using var database = CreateContext();
        string generated = database.Database.GenerateCreateScript();
        string scriptPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
            "../../../../NumberForge.Api/Persistence/Schema/001_game_state.sql"));
        string checkedIn = File.ReadAllText(scriptPath);

        // Compare the table definitions as sets of columns/constraints, since SQL column order
        // need not match C# declaration order. Also compare every index used by the provider.
        Assert.Equal(TableDefinitions(generated), TableDefinitions(checkedIn));
        Assert.Equal(IndexDefinitions(generated), IndexDefinitions(checkedIn));
    }

    [Fact]
    public void Registration_CreatesIndependentPooledContextsWithoutConnecting()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSqlServerGameState(ModelConnection);
        using var provider = services.BuildServiceProvider();
        Assert.IsType<SqlServerGameStateRepository>(provider.GetRequiredService<IGameStateRepository>());
        var factory = provider.GetRequiredService<IDbContextFactory<GameStateDbContext>>();
        using var first = factory.CreateDbContext();
        using var second = factory.CreateDbContext();
        Assert.NotSame(first, second);
        Assert.Equal("Microsoft.EntityFrameworkCore.SqlServer", first.Database.ProviderName);
        Assert.Equal(15, first.Database.GetCommandTimeout());
        Assert.False(first.Database.CreateExecutionStrategy().RetriesOnFailure);
    }

    [Theory]
    [InlineData("")]
    [InlineData("Server=sql.example.invalid;Encrypt=true")]
    [InlineData("Database=NumberForge;Encrypt=true")]
    [InlineData("Server=sql.example.invalid;Database=NumberForge;Encrypt=false")]
    [InlineData("Server=sql.example.invalid;Database=NumberForge;TrustServerCertificate=true")]
    public void Registration_RejectsMissingDatabaseOrUnverifiedTransport(string connection)
    {
        Assert.ThrowsAny<ArgumentException>(() => new ServiceCollection().AddSqlServerGameState(connection));
    }

    [Fact]
    public async Task InvalidWrites_StopBeforeOpeningADatabaseConnection()
    {
        var repository = new SqlServerGameStateRepository(new NoConnectionFactory());
        var session = ExampleSession();

        Assert.False(await repository.TryCreateAsync(session with { Revision = 1 }, 10, 100, Now));
        Assert.False(await repository.TryCreateAsync(session with { StartedAtUtc = Now.AddMinutes(1) }, 10, 100, Now));
        Assert.False(await repository.TrySaveAsync(session with { Revision = 2 }, 0, null, Now));
        Assert.False(await repository.TrySaveAsync(session with { Revision = 1 }, 0, null, session.PlayUntilUtc!.Value));
        Assert.False(await repository.TryCreateAsync(session, 10, 100, session.ExpiresAtUtc));
        await Assert.ThrowsAsync<ArgumentException>(() => repository.TryCreateAsync(session with
        {
            Payload = new string('x', GameStateDbContext.MaximumPayloadCharacters + 1)
        }, 10, 100, Now));
        await Assert.ThrowsAsync<ArgumentException>(() => repository.TrySaveAsync(session with { Revision = 1 }, 0,
            new RouteDocument("another-player", "collection", "route", 2, Now, Now.AddHours(1)), Now));
        await Assert.ThrowsAsync<ArgumentException>(() => repository.FindAsync(session.Id, "owner ", Now));
    }

    private static GameStateDbContext CreateContext() => new(new DbContextOptionsBuilder<GameStateDbContext>()
        .UseSqlServer(ModelConnection).Options);

    private static SessionDocument ExampleSession() => new(Guid.NewGuid(), "player", Now, Now.AddHours(2),
        Now.AddHours(1), 0, "{}");

    private static string[] TableDefinitions(string script)
        => Regex.Matches(script, @"CREATE TABLE \[numberforge\]\.\[(.*?)\]\s*\((.*?)\);", RegexOptions.Singleline)
            .Select(match => match.Groups[1].Value + ":" + string.Join("|", TableParts(match.Groups[2].Value)
                .Select(NormalizeSql).Order(StringComparer.Ordinal)))
            .Order(StringComparer.Ordinal).ToArray();

    private static IEnumerable<string> TableParts(string definition)
    {
        int depth = 0;
        int start = 0;
        for (int index = 0; index < definition.Length; index++)
        {
            if (definition[index] == '(') depth++;
            if (definition[index] == ')') depth--;
            if (definition[index] == ',' && depth == 0)
            {
                yield return definition[start..index];
                start = index + 1;
            }
        }
        yield return definition[start..];
    }

    private static string[] IndexDefinitions(string script)
        => Regex.Matches(script, @"CREATE INDEX .*?;", RegexOptions.Singleline)
            .Select(match => NormalizeSql(match.Value)).Order(StringComparer.Ordinal).ToArray();

    private static string NormalizeSql(string sql) => Regex.Replace(sql, @"\s+", "").ToUpperInvariant();

    private sealed class NoConnectionFactory : IDbContextFactory<GameStateDbContext>
    {
        public GameStateDbContext CreateDbContext()
            => throw new InvalidOperationException("Invalid input must not open a database context.");
    }
}
