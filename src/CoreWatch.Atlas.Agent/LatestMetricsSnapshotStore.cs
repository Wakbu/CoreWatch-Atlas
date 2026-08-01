using CoreWatch.Atlas.Contracts;

namespace CoreWatch.Atlas.Agent;

public sealed class LatestMetricsSnapshotStore
{
    private SystemMetricsSnapshot? latest;

    public SystemMetricsSnapshot? Latest => Volatile.Read(ref latest);

    public void Update(SystemMetricsSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        Volatile.Write(ref latest, snapshot);
    }
}
// CoreWatch Atlas module: LatestMetricsSnapshotStore.
