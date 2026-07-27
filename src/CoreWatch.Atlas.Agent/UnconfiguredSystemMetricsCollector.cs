using System.Runtime.InteropServices;
using CoreWatch.Atlas.Contracts;

namespace CoreWatch.Atlas.Agent;

internal sealed class UnconfiguredSystemMetricsCollector : ISystemMetricsCollector
{
    public string Platform => RuntimeInformation.OSDescription;

    public ValueTask<SystemMetricsSnapshot> CaptureAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return ValueTask.FromException<SystemMetricsSnapshot>(
            new NotSupportedException(
                "No operating-system metrics collector has been configured."));
    }
}
