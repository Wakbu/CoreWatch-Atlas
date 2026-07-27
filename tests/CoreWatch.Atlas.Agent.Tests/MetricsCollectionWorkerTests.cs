using System.Collections.Concurrent;
using CoreWatch.Atlas.Agent;
using CoreWatch.Atlas.Contracts;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CoreWatch.Atlas.Agent.Tests;

[TestClass]
public sealed class MetricsCollectionWorkerTests
{
    [TestMethod]
    public void ServicesRegisterConfiguredCollectorAndWorker()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [$"{MetricsCollectionOptions.SectionName}:Interval"] = "00:00:01",
            })
            .Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddAtlasMetricsCollection<FakeCollector>(configuration);

        using var provider = services.BuildServiceProvider();

        Assert.IsInstanceOfType<FakeCollector>(
            provider.GetRequiredService<ISystemMetricsCollector>());
        Assert.AreEqual(
            TimeSpan.FromSeconds(1),
            provider.GetRequiredService<IOptions<MetricsCollectionOptions>>()
                .Value.Interval);
        Assert.IsTrue(provider.GetServices<IHostedService>()
            .Any(static service => service is MetricsCollectionWorker));
    }

    [TestMethod]
    public async Task WorkerCollectsAgainAfterFailureAndLogsStructuredError()
    {
        var secondCapture = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var collector = new FakeCollector
        {
            Capture = (attempt, _) =>
            {
                if (attempt == 1)
                {
                    throw new InvalidOperationException("Test failure.");
                }

                secondCapture.TrySetResult();
                return ValueTask.FromResult(CreateSnapshot());
            },
        };
        var logger = new RecordingLogger<MetricsCollectionWorker>();
        var worker = CreateWorker(collector, logger, TimeSpan.FromMilliseconds(10));

        await worker.StartAsync(CancellationToken.None);
        await secondCapture.Task.WaitAsync(TimeSpan.FromSeconds(3));
        await worker.StopAsync(CancellationToken.None);

        Assert.IsGreaterThanOrEqualTo(2, collector.CaptureCount);
        var error = logger.Entries.Single(entry => entry.EventId.Id == 1002);
        Assert.AreEqual(LogLevel.Error, error.Level);
        Assert.AreEqual("test", error.Properties["Platform"]);
        Assert.IsInstanceOfType<InvalidOperationException>(error.Exception);
    }

    [TestMethod]
    public async Task StopCancelsAnActiveCapture()
    {
        var captureStarted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var cancellationObserved = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var collector = new FakeCollector
        {
            Capture = async (_, cancellationToken) =>
            {
                captureStarted.TrySetResult();

                try
                {
                    await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                }
                finally
                {
                    cancellationObserved.TrySetResult();
                }

                return CreateSnapshot();
            },
        };
        var worker = CreateWorker(
            collector,
            new RecordingLogger<MetricsCollectionWorker>(),
            TimeSpan.FromSeconds(1));

        await worker.StartAsync(CancellationToken.None);
        await captureStarted.Task.WaitAsync(TimeSpan.FromSeconds(3));
        await worker.StopAsync(CancellationToken.None);
        await cancellationObserved.Task.WaitAsync(TimeSpan.FromSeconds(3));

        Assert.AreEqual(1, collector.CaptureCount);
    }

    [TestMethod]
    public void WorkerRejectsNonPositiveInterval()
    {
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(
            () => CreateWorker(
                new FakeCollector(),
                new RecordingLogger<MetricsCollectionWorker>(),
                TimeSpan.Zero));
    }

    private static MetricsCollectionWorker CreateWorker(
        ISystemMetricsCollector collector,
        ILogger<MetricsCollectionWorker> logger,
        TimeSpan interval)
    {
        return new MetricsCollectionWorker(
            collector,
            logger,
            Options.Create(new MetricsCollectionOptions { Interval = interval }),
            TimeProvider.System);
    }

    private static SystemMetricsSnapshot CreateSnapshot()
    {
        return new SystemMetricsSnapshot(
            DateTimeOffset.UnixEpoch,
            TimeSpan.FromHours(1),
            new AgentIdentity("agent-test", "host", "Test OS", "x64", "1.0.0"),
            new CpuMetrics(0.25, 4),
            new MemoryMetrics(1_000, 500),
            [],
            [],
            []);
    }

    public sealed class FakeCollector : ISystemMetricsCollector
    {
        private int _captureCount;

        public string Platform => "test";

        public int CaptureCount => _captureCount;

        public Func<int, CancellationToken, ValueTask<SystemMetricsSnapshot>> Capture
        {
            get;
            init;
        } = static (_, _) => ValueTask.FromResult(CreateSnapshot());

        public ValueTask<SystemMetricsSnapshot> CaptureAsync(
            CancellationToken cancellationToken = default)
        {
            var attempt = Interlocked.Increment(ref _captureCount);
            return Capture(attempt, cancellationToken);
        }
    }

    private sealed class RecordingLogger<T> : ILogger<T>
    {
        public ConcurrentQueue<LogEntry> Entries { get; } = new();

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => NullScope.Instance;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            var properties = state is IEnumerable<KeyValuePair<string, object?>> values
                ? values.ToDictionary(static item => item.Key, static item => item.Value)
                : new Dictionary<string, object?>();

            Entries.Enqueue(new LogEntry(logLevel, eventId, exception, properties));
        }
    }

    private sealed record LogEntry(
        LogLevel Level,
        EventId EventId,
        Exception? Exception,
        IReadOnlyDictionary<string, object?> Properties);

    private sealed class NullScope : IDisposable
    {
        public static NullScope Instance { get; } = new();

        public void Dispose()
        {
        }
    }
}
