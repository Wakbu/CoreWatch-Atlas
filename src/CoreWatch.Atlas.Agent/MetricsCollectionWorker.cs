using CoreWatch.Atlas.Contracts;
using Microsoft.Extensions.Options;

namespace CoreWatch.Atlas.Agent;

public sealed class MetricsCollectionWorker : BackgroundService
{
    private readonly ISystemMetricsCollector _collector;
    private readonly ILogger<MetricsCollectionWorker> _logger;
    private readonly TimeProvider _timeProvider;
    private readonly TimeSpan _interval;

    public MetricsCollectionWorker(
        ISystemMetricsCollector collector,
        ILogger<MetricsCollectionWorker> logger,
        IOptions<MetricsCollectionOptions> options,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(collector);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(timeProvider);

        var interval = options.Value.Interval;
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(interval, TimeSpan.Zero);

        _collector = collector;
        _logger = logger;
        _timeProvider = timeProvider;
        _interval = interval;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        MetricsCollectionLog.Started(_logger, _collector.Platform, _interval);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var snapshot = await _collector.CaptureAsync(stoppingToken);
                MetricsCollectionLog.Captured(
                    _logger,
                    snapshot.Agent.AgentId,
                    _collector.Platform,
                    snapshot.CapturedAtUtc);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                MetricsCollectionLog.Failed(_logger, exception, _collector.Platform);
            }

            try
            {
                await Task.Delay(_interval, _timeProvider, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }

        MetricsCollectionLog.Stopped(_logger, _collector.Platform);
    }
}
