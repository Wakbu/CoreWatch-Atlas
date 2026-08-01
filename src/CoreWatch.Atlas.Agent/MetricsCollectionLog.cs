namespace CoreWatch.Atlas.Agent;

internal static partial class MetricsCollectionLog
{
    [LoggerMessage(
        EventId = 1000,
        Level = LogLevel.Information,
        Message = "Metrics collection started for {Platform} with interval {Interval}.")]
    public static partial void Started(
        ILogger logger,
        string platform,
        TimeSpan interval);

    [LoggerMessage(
        EventId = 1001,
        Level = LogLevel.Information,
        Message = "Metrics captured for agent {AgentId} on {Platform} at {CapturedAtUtc}.")]
    public static partial void Captured(
        ILogger logger,
        string agentId,
        string platform,
        DateTimeOffset capturedAtUtc);

    [LoggerMessage(
        EventId = 1002,
        Level = LogLevel.Error,
        Message = "Metrics capture failed on {Platform}; the next interval will retry.")]
    public static partial void Failed(
        ILogger logger,
        Exception exception,
        string platform);

    [LoggerMessage(
        EventId = 1003,
        Level = LogLevel.Information,
        Message = "Metrics collection stopped for {Platform}.")]
    public static partial void Stopped(ILogger logger, string platform);

    [LoggerMessage(
        EventId = 1004,
        Level = LogLevel.Error,
        Message = "Local metrics output failed; collection will continue.")]
    public static partial void OutputFailed(ILogger logger, Exception exception);

    [LoggerMessage(
        EventId = 1005,
        Level = LogLevel.Error,
        Message = "Atlas server transmission failed; local collection will continue.")]
    public static partial void ServerTransmissionFailed(
        ILogger logger,
        Exception exception);

    [LoggerMessage(
        EventId = 1010,
        Level = LogLevel.Information,
        Message = "Prometheus endpoint listening at {Url}/metrics.")]
    public static partial void PrometheusStarted(ILogger logger, string url);
}
// CoreWatch Atlas module: MetricsCollectionLog.
