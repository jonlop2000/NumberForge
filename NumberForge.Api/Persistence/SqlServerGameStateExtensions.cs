using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace NumberForge.Api.Persistence;

public static class SqlServerGameStateExtensions
{
    public static IServiceCollection AddSqlServerGameState(this IServiceCollection services, string connectionString)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        var connection = new SqlConnectionStringBuilder(connectionString);
        if (string.IsNullOrWhiteSpace(connection.DataSource) || string.IsNullOrWhiteSpace(connection.InitialCatalog))
        {
            throw new ArgumentException("The game database connection must name a SQL Server and database.", nameof(connectionString));
        }
        if (connection.Encrypt == SqlConnectionEncryptOption.Optional || connection.TrustServerCertificate)
        {
            throw new ArgumentException("The game database requires encryption and a trusted server certificate.", nameof(connectionString));
        }

        // A singleton repository uses a separate pooled context per operation. DbContext itself
        // is not thread-safe and must never be shared by simultaneous HTTP requests.
        services.AddPooledDbContextFactory<GameStateDbContext>(options =>
            options.UseSqlServer(connection.ConnectionString, sql => sql.CommandTimeout(15)));
        services.AddSingleton<IGameStateRepository, SqlServerGameStateRepository>();
        return services;
    }
}
