using Microsoft.EntityFrameworkCore;

namespace NumberForge.Api.Persistence;

// These rows are storage details. Controllers continue to use the repository contract,
// and the game rules stay in NumberForge.Core instead of depending on Entity Framework.
public sealed class GameStateDbContext(DbContextOptions<GameStateDbContext> options) : DbContext(options)
{
    public const int MaximumPayloadCharacters = 65_536;
    public const string IdentifierCollation = "Latin1_General_100_BIN2";

    public DbSet<GameSessionEntity> Sessions => Set<GameSessionEntity>();
    public DbSet<DiscoveredRouteEntity> Routes => Set<DiscoveredRouteEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var sessions = modelBuilder.Entity<GameSessionEntity>();
        sessions.ToTable("Sessions", "numberforge", table =>
        {
            table.HasCheckConstraint("CK_Sessions_Revision", "[Revision] >= 0");
            table.HasCheckConstraint("CK_Sessions_Lifetime", "[ExpiresAtUtc] > [StartedAtUtc]");
            table.HasCheckConstraint("CK_Sessions_PlayWindow",
                "[PlayUntilUtc] IS NULL OR ([PlayUntilUtc] > [StartedAtUtc] AND [PlayUntilUtc] <= [ExpiresAtUtc])");
            table.HasCheckConstraint("CK_Sessions_Payload",
                "ISJSON([Payload]) = 1 AND DATALENGTH([Payload]) <= 131072");
        });
        sessions.HasKey(session => session.Id);
        sessions.Property(session => session.Id).ValueGeneratedNever();
        sessions.Property(session => session.PlayerId).HasMaxLength(64).IsUnicode(false)
            .UseCollation(IdentifierCollation);
        sessions.Property(session => session.Payload).IsRequired();
        sessions.Property(session => session.Revision).IsConcurrencyToken();
        // These two indexes support admission checks and bounded expiry cleanup.
        sessions.HasIndex(session => new { session.PlayerId, session.ExpiresAtUtc });
        sessions.HasIndex(session => session.ExpiresAtUtc);

        var routes = modelBuilder.Entity<DiscoveredRouteEntity>();
        routes.ToTable("Routes", "numberforge", table =>
        {
            table.HasCheckConstraint("CK_Routes_MoveCount", "[MoveCount] > 0");
            table.HasCheckConstraint("CK_Routes_Lifetime", "[ExpiresAtUtc] > [FoundAtUtc]");
        });
        // This database key makes discovery of the same route idempotent across servers.
        routes.HasKey(route => new { route.PlayerId, route.CollectionKey, route.RouteId });
        routes.Property(route => route.PlayerId).HasMaxLength(64).IsUnicode(false)
            .UseCollation(IdentifierCollation);
        routes.Property(route => route.CollectionKey).HasMaxLength(64).IsUnicode(false)
            .UseCollation(IdentifierCollation);
        routes.Property(route => route.RouteId).HasMaxLength(64).IsUnicode(false)
            .UseCollation(IdentifierCollation);
        routes.HasIndex(route => route.ExpiresAtUtc);
    }
}

public sealed class GameSessionEntity
{
    public Guid Id { get; set; }
    public required string PlayerId { get; set; }
    public DateTimeOffset StartedAtUtc { get; set; }
    public DateTimeOffset ExpiresAtUtc { get; set; }
    public DateTimeOffset? PlayUntilUtc { get; set; }
    public int Revision { get; set; }
    public required string Payload { get; set; }
}

public sealed class DiscoveredRouteEntity
{
    public required string PlayerId { get; set; }
    public required string CollectionKey { get; set; }
    public required string RouteId { get; set; }
    public int MoveCount { get; set; }
    public DateTimeOffset FoundAtUtc { get; set; }
    public DateTimeOffset ExpiresAtUtc { get; set; }
}
