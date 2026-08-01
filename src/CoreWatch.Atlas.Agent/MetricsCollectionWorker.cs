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
    private readonly DiagnosticsConfigurationStore diagnostics;
    private IReadOnlyList<MonitoredServiceMetrics> lastServices=[];
    private IReadOnlyList<DiagnosticCheckMetrics> lastChecks=[];
    private DateTimeOffset nextDiagnosticsAt;

    // 기존 호스트/테스트 코드가 사용하던 생성자는 빈 진단 설정으로 유지한다.
    public MetricsCollectionWorker(
        ISystemMetricsCollector collector,
        ILogger<MetricsCollectionWorker> logger,
        MetricsSnapshotPublisher publisher,
        IOptions<MetricsCollectionOptions> options,
        TimeProvider timeProvider)
        : this(collector, logger, publisher, options, timeProvider,
            new DiagnosticsConfigurationStore(Options.Create(new DiagnosticsOptions())))
    {
    }

    public MetricsCollectionWorker(
        ISystemMetricsCollector collector,
        ILogger<MetricsCollectionWorker> logger,
        MetricsSnapshotPublisher publisher,
        IOptions<MetricsCollectionOptions> options,
        TimeProvider timeProvider,
        DiagnosticsConfigurationStore diagnostics)
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
        this.diagnostics = diagnostics;
        interval = configuredInterval;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        MetricsCollectionLog.Started(logger, collector.Platform, interval);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var captured = await collector.CaptureAsync(stoppingToken);
                if(timeProvider.GetUtcNow()>=nextDiagnosticsAt)
                {
                    var currentDiagnostics=diagnostics.Current;
                    try{lastServices=await ServiceDiagnostics.ReadAsync(currentDiagnostics.Services,stoppingToken);}catch(Exception e)when(e is not OperationCanceledException){logger.LogWarning(e,"Service diagnostics failed.");}
                    try{lastChecks=await DiagnosticChecks.RunAsync(currentDiagnostics,stoppingToken);}catch(Exception e)when(e is not OperationCanceledException){logger.LogWarning(e,"Diagnostic checks failed.");}
                    nextDiagnosticsAt=timeProvider.GetUtcNow().AddMinutes(1);
                }
                var snapshot = new SystemMetricsSnapshot(captured.CapturedAtUtc,captured.Uptime,captured.Agent,captured.Cpu,captured.Memory,captured.FileSystems,captured.Disks,captured.NetworkInterfaces,lastServices,lastChecks);
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
