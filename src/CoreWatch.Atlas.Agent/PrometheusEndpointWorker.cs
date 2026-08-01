using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

namespace CoreWatch.Atlas.Agent;

internal sealed class PrometheusEndpointWorker : BackgroundService
{
    internal const string ContentType =
        "text/plain; version=0.0.4; charset=utf-8";

    private readonly LatestMetricsSnapshotStore store;
    private readonly ILogger<PrometheusEndpointWorker> logger;
    private readonly PrometheusEndpointOptions options;

    public PrometheusEndpointWorker(
        LatestMetricsSnapshotStore store,
        ILogger<PrometheusEndpointWorker> logger,
        IOptions<LocalOutputOptions> options)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(options);

        this.store = store;
        this.logger = logger;
        this.options = options.Value.Prometheus;
        Validate(this.options);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Enabled)
        {
            return;
        }

        var builder = WebApplication.CreateSlimBuilder(
            new WebApplicationOptions { Args = [] });
        builder.Logging.ClearProviders();
        builder.WebHost.UseUrls(options.Url);
        builder.WebHost.ConfigureKestrel(
            static serverOptions => serverOptions.AddServerHeader = false);

        await using var application = builder.Build();
        application.MapGet("/metrics", WriteMetricsAsync);

        MetricsCollectionLog.PrometheusStarted(logger, options.Url);
        await application.RunAsync(stoppingToken).ConfigureAwait(false);
    }

    internal async Task WriteMetricsAsync(HttpContext context)
    {
        var snapshot = store.Latest;
        context.Response.ContentType = ContentType;

        if (snapshot is null)
        {
            context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
            await context.Response.WriteAsync(
                "No metrics snapshot has been captured yet.\n",
                context.RequestAborted).ConfigureAwait(false);
            return;
        }

        await context.Response.WriteAsync(
            PrometheusMetricsFormatter.Format(snapshot),
            context.RequestAborted).ConfigureAwait(false);
    }

    private static void Validate(PrometheusEndpointOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (!options.Enabled)
        {
            return;
        }

        if (!Uri.TryCreate(options.Url, UriKind.Absolute, out var uri)
            || uri.Scheme != Uri.UriSchemeHttp
            || !string.IsNullOrEmpty(uri.AbsolutePath.Trim('/'))
            || !string.IsNullOrEmpty(uri.Query)
            || !string.IsNullOrEmpty(uri.Fragment))
        {
            throw new InvalidOperationException(
                "Prometheus URL must be an absolute HTTP origin without a path, query, or fragment.");
        }
    }
}
// CoreWatch Atlas module: PrometheusEndpointWorker.
