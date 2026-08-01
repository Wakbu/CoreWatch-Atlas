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
        services.Configure<ServerTransmissionOptions>(
            configuration.GetSection(ServerTransmissionOptions.SectionName));
        services.Configure<DiagnosticsOptions>(configuration.GetSection(DiagnosticsOptions.SectionName));
        services
            .AddOptions<AutomaticUpdateOptions>()
            .Bind(configuration.GetSection(AutomaticUpdateOptions.SectionName))
            .Validate(
                options => options.CheckInterval >= TimeSpan.FromMinutes(1)
                    && options.CheckInterval <= TimeSpan.FromDays(1),
                "Automatic update CheckInterval must be between one minute and one day.")
            .Validate(
                options => !string.IsNullOrWhiteSpace(options.StatePath),
                "Automatic update StatePath must not be empty.")
            .ValidateOnStart();
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<TextWriter>(static _ => Console.Out);
        services.TryAddSingleton<LatestMetricsSnapshotStore>();
        services.TryAddSingleton<DiagnosticsConfigurationStore>();
        services.TryAddSingleton(
            static _ => new HttpClient(new SocketsHttpHandler
            {
                PooledConnectionLifetime = TimeSpan.FromMinutes(5),
            })
            {
                Timeout = TimeSpan.FromSeconds(10),
            });
        services.TryAddSingleton<AtlasServerClient>();
        services.TryAddSingleton<MetricsSnapshotPublisher>();
        services.AddSingleton<ISystemMetricsCollector, TCollector>();
        services.AddHostedService<MetricsCollectionWorker>();
        services.AddHostedService<PrometheusEndpointWorker>();
        services.AddHostedService<AgentUpdateWorker>();
        services.AddHostedService<AgentCommandWorker>();
        services.AddHostedService<DiagnosticsConfigurationWorker>();

        return services;
    }
}
