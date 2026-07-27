using System.Text.Json;
using CoreWatch.Atlas.Contracts;
using Microsoft.Extensions.Options;

namespace CoreWatch.Atlas.Agent;

public sealed class MetricsSnapshotPublisher
{
    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web);

    private readonly LatestMetricsSnapshotStore store;
    private readonly TextWriter output;
    private readonly bool jsonEnabled;

    public MetricsSnapshotPublisher(
        LatestMetricsSnapshotStore store,
        TextWriter output,
        IOptions<LocalOutputOptions> options)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(options);

        this.store = store;
        this.output = output;
        jsonEnabled = options.Value.JsonEnabled;
    }

    public async ValueTask PublishAsync(
        SystemMetricsSnapshot snapshot,
        CancellationToken cancellationToken)
    {
        store.Update(snapshot);
        if (!jsonEnabled)
        {
            return;
        }

        var json = JsonSerializer.Serialize(snapshot, JsonOptions);
        await output.WriteLineAsync(json.AsMemory(), cancellationToken)
            .ConfigureAwait(false);
        await output.FlushAsync(cancellationToken).ConfigureAwait(false);
    }
}
