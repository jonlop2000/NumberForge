using NumberForge.Api.Persistence;
using NumberForge.Api.Security;
using NumberForge.Api.Services;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddControllers();
builder.AddApiSecurity();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddOptions<SessionLimits>().BindConfiguration("SessionLimits")
    .Validate(limits => limits.MaxSessionsPerPlayer is >= 20 and <= 1000 &&
        limits.MaxSessionsTotal >= limits.MaxSessionsPerPlayer && limits.MaxSessionsTotal <= 1_000_000,
        "Session limits must be bounded and allow normal replay.")
    .ValidateOnStart();

string? connectionString = builder.Configuration.GetConnectionString("NumberForge");
if (!string.IsNullOrWhiteSpace(connectionString))
{
    // Schema migrations are an explicit deployment step, never a side effect of an HTTP request.
    builder.Services.AddSqlServerGameState(connectionString);
}
else if (builder.Environment.IsDevelopment())
{
    builder.Services.AddSingleton<IGameStateRepository, InMemoryGameStateRepository>();
}
else
{
    throw new InvalidOperationException("Production requires ConnectionStrings:NumberForge for durable SQL Server storage.");
}
builder.Services.AddSingleton<DailyPuzzleService>();
builder.Services.AddSingleton<PuzzleCatalog>();
builder.Services.AddSingleton<GameSessionStore>();
builder.Services.AddSingleton<PuzzleResponseMapper>();
builder.Services.AddHostedService<GameStateCleanupService>();

var app = builder.Build();
if (string.IsNullOrWhiteSpace(connectionString))
    app.Logger.LogWarning("Development memory storage is active. Sessions and discoveries reset when this process stops.");
app.UseApiSecurity();
app.MapControllers();
app.Run();

// Allows the real HTTP pipeline to be exercised with WebApplicationFactory in integration tests.
public partial class Program { }
