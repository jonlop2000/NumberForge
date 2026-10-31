using NumberForge.Api.Persistence;

namespace NumberForge.Api.Services;

// Storage retention is enforced on every read. This worker also reclaims expired rows in small batches.
public sealed class GameStateCleanupService(IGameStateRepository repository, TimeProvider clock, ILogger<GameStateCleanupService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1), clock);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try { await repository.CleanupAsync(clock.GetUtcNow(), stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception error) { logger.LogError(error, "Expired game state cleanup failed."); }
        }
    }
}
