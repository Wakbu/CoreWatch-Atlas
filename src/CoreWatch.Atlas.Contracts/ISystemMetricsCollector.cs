namespace CoreWatch.Atlas.Contracts;

public interface ISystemMetricsCollector
{
    string Platform { get; }

    ValueTask<SystemMetricsSnapshot> CaptureAsync(
        CancellationToken cancellationToken = default);
}
// CoreWatch Atlas module: ISystemMetricsCollector.
