using CoreWatch.Atlas.Contracts;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace CoreWatch.Atlas.Agent;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddAtlasMetricsCollection<TCollector>(
        this IServiceCollection services,
        IConfiguration configuration)
        where TCollector : class, ISystemMetricsCollector
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.Configure<MetricsCollectionOptions>(
            configuration.GetSection(MetricsCollectionOptions.SectionName));
        services.Configure<LocalOutputOptions>(
            configuration.GetSection(LocalOutputOptions.SectionName));
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<TextWriter>(static _ => Console.Out);
        services.TryAddSingleton<LatestMetricsSnapshotStore>();
        services.TryAddSingleton<MetricsSnapshotPublisher>();
        services.AddSingleton<ISystemMetricsCollector, TCollector>();
        services.AddHostedService<MetricsCollectionWorker>();
        services.AddHostedService<PrometheusEndpointWorker>();

        return services;
    }
}
