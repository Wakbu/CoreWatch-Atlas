using Microsoft.Extensions.Options;

namespace CoreWatch.Atlas.Server;

public sealed class SnapshotRetentionWorker(
    AtlasDatabase database,
    IOptions<ServerApiOptions> options,
    TimeProvider timeProvider,
    ILogger<SnapshotRetentionWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var settings = options.Value;
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var cutoff = timeProvider.GetUtcNow()
                    .AddDays(-settings.SnapshotRetentionDays);
                var deleted = await database.DeleteSnapshotsOlderThanAsync(
                    cutoff,
                    stoppingToken);
                if (deleted > 0)
                {
                    logger.LogInformation(
                        "Deleted {SnapshotCount} expired Atlas snapshots.",
                        deleted);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Atlas snapshot retention cleanup failed.");
            }

            try
            {
                await Task.Delay(
                    TimeSpan.FromMinutes(settings.CleanupIntervalMinutes),
                    timeProvider,
                    stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }
    }
}
// CoreWatch Atlas module: SnapshotRetentionWorker.
