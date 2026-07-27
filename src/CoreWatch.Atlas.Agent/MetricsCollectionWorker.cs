using CoreWatch.Atlas.Contracts;
using Microsoft.Extensions.Options;

namespace CoreWatch.Atlas.Agent;

public sealed class MetricsCollectionWorker : BackgroundService
{
    private readonly ISystemMetricsCollector collector;
    private readonly ILogger<MetricsCollectionWorker> logger;
    private readonly MetricsSnapshotPublisher publisher;
    private readonly TimeProvider timeProvider;
    private readonly TimeSpan interval;

    public MetricsCollectionWorker(
        ISystemMetricsCollector collector,
        ILogger<MetricsCollectionWorker> logger,
        MetricsSnapshotPublisher publisher,
        IOptions<MetricsCollectionOptions> options,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(collector);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(publisher);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(timeProvider);

        var configuredInterval = options.Value.Interval;
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(
            configuredInterval,
            TimeSpan.Zero);

        this.collector = collector;
        this.logger = logger;
        this.publisher = publisher;
        this.timeProvider = timeProvider;
        interval = configuredInterval;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        MetricsCollectionLog.Started(logger, collector.Platform, interval);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var snapshot = await collector.CaptureAsync(stoppingToken);
                MetricsCollectionLog.Captured(
                    logger,
                    snapshot.Agent.AgentId,
                    collector.Platform,
                    snapshot.CapturedAtUtc);

                try
                {
                    await publisher.PublishAsync(snapshot, stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception exception)
                {
                    MetricsCollectionLog.OutputFailed(logger, exception);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                MetricsCollectionLog.Failed(logger, exception, collector.Platform);
            }

            try
            {
                await Task.Delay(interval, timeProvider, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }

        MetricsCollectionLog.Stopped(logger, collector.Platform);
    }
}
