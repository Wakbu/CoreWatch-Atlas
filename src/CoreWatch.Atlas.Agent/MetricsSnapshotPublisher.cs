using System.Text.Json;
using CoreWatch.Atlas.Contracts;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace CoreWatch.Atlas.Agent;

public sealed class MetricsSnapshotPublisher
{
    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web);

    private readonly LatestMetricsSnapshotStore store;
    private readonly TextWriter output;
    private readonly bool jsonEnabled;
    private readonly AtlasServerClient? serverClient;
    private readonly ILogger<MetricsSnapshotPublisher> logger;

    public MetricsSnapshotPublisher(
        LatestMetricsSnapshotStore store,
        TextWriter output,
        IOptions<LocalOutputOptions> options,
        AtlasServerClient? serverClient = null,
        ILogger<MetricsSnapshotPublisher>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(options);

        this.store = store;
        this.output = output;
        this.serverClient = serverClient;
        this.logger = logger ?? NullLogger<MetricsSnapshotPublisher>.Instance;
        jsonEnabled = options.Value.JsonEnabled;
    }

    public async ValueTask PublishAsync(
        SystemMetricsSnapshot snapshot,
        CancellationToken cancellationToken)
    {
        store.Update(snapshot);
        if (serverClient is not null)
        {
            try
            {
                await serverClient.SendAsync(snapshot, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                MetricsCollectionLog.ServerTransmissionFailed(logger, exception);
            }
        }

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
