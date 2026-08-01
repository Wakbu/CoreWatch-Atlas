using System.Globalization;
using CoreWatch.Atlas.Contracts;

namespace CoreWatch.Atlas.Collectors.Linux;

internal static class LinuxProcParser
{
    private const ulong Kibibyte = 1024;
    private const ulong DiskSectorBytes = 512;

    public static CpuSample ParseCpuSample(string content)
    {
        var lines = Lines(content);
        var aggregate = lines.FirstOrDefault(
            static line => line.StartsWith("cpu ", StringComparison.Ordinal));
        if (aggregate is null)
        {
            throw Invalid("/proc/stat does not contain an aggregate CPU row.");
        }

        var fields = Split(aggregate);
        if (fields.Length < 5)
        {
            throw Invalid("/proc/stat aggregate CPU row is incomplete.");
        }

        ulong total = 0;
        for (var index = 1; index < Math.Min(fields.Length, 9); index++)
        {
            total = checked(total + ParseUInt64(fields[index], "/proc/stat"));
        }

        var idle = checked(
            ParseUInt64(fields[4], "/proc/stat")
            + (fields.Length > 5 ? ParseUInt64(fields[5], "/proc/stat") : 0));
        var logicalProcessors = lines.Count(static line =>
            line.Length > 3
            && line.StartsWith("cpu", StringComparison.Ordinal)
            && char.IsAsciiDigit(line[3]));
        if (logicalProcessors < 1)
        {
            throw Invalid("/proc/stat does not contain logical CPU rows.");
        }

        return new CpuSample(total, idle, logicalProcessors);
    }

    public static CpuMetrics CalculateCpu(CpuSample first, CpuSample second)
    {
        if (second.Total <= first.Total || second.Idle < first.Idle)
        {
            throw Invalid("CPU counters did not advance monotonically.");
        }

        var totalDelta = second.Total - first.Total;
        var idleDelta = second.Idle - first.Idle;
        if (idleDelta > totalDelta)
        {
            throw Invalid("CPU idle counter delta exceeds total counter delta.");
        }

        return new CpuMetrics(
            (double)(totalDelta - idleDelta) / totalDelta,
            second.LogicalProcessorCount);
    }

    public static MemoryMetrics ParseMemory(string content)
    {
        var values = new Dictionary<string, ulong>(StringComparer.Ordinal);
        foreach (var line in Lines(content))
        {
            var separator = line.IndexOf(':');
            if (separator <= 0)
            {
                continue;
            }

            var key = line[..separator];
            var fields = Split(line[(separator + 1)..]);
            if (fields.Length > 0)
            {
                values[key] = checked(ParseUInt64(fields[0], "/proc/meminfo") * Kibibyte);
            }
        }

        if (!values.TryGetValue("MemTotal", out var total))
        {
            throw Invalid("/proc/meminfo does not contain MemTotal.");
        }

        if (!values.TryGetValue("MemAvailable", out var available))
        {
            available = checked(
                Get(values, "MemFree") + Get(values, "Buffers") + Get(values, "Cached"));
        }

        return new MemoryMetrics(total, available);
    }

    public static IReadOnlyList<DiskIoMetrics> ParseDiskIo(string content)
    {
        var metrics = new List<DiskIoMetrics>();
        foreach (var line in Lines(content))
        {
            var fields = Split(line);
            if (fields.Length < 10)
            {
                throw Invalid("/proc/diskstats contains an incomplete row.");
            }

            metrics.Add(new DiskIoMetrics(
                fields[2],
                checked(ParseUInt64(fields[5], "/proc/diskstats") * DiskSectorBytes),
                checked(ParseUInt64(fields[9], "/proc/diskstats") * DiskSectorBytes)));
        }

        return metrics;
    }

    public static IReadOnlyList<NetworkInterfaceMetrics> ParseNetwork(string content)
    {
        var metrics = new List<NetworkInterfaceMetrics>();
        foreach (var line in Lines(content))
        {
            var separator = line.IndexOf(':');
            if (separator < 0)
            {
                continue;
            }

            var name = line[..separator].Trim();
            var fields = Split(line[(separator + 1)..]);
            if (name.Length == 0 || fields.Length < 9)
            {
                throw Invalid("/proc/net/dev contains an incomplete interface row.");
            }

            metrics.Add(new NetworkInterfaceMetrics(
                name,
                ParseUInt64(fields[0], "/proc/net/dev"),
                ParseUInt64(fields[8], "/proc/net/dev")));
        }

        return metrics;
    }

    public static TimeSpan ParseUptime(string content)
    {
        var fields = Split(content);
        if (fields.Length == 0
            || !double.TryParse(fields[0], NumberStyles.AllowDecimalPoint,
                CultureInfo.InvariantCulture, out var seconds)
            || !double.IsFinite(seconds)
            || seconds < 0)
        {
            throw Invalid("/proc/uptime does not contain a valid uptime value.");
        }

        return TimeSpan.FromSeconds(seconds);
    }

    private static ulong Get(IReadOnlyDictionary<string, ulong> values, string key)
    {
        if (!values.TryGetValue(key, out var value))
        {
            throw Invalid($"/proc/meminfo does not contain {key}.");
        }

        return value;
    }

    private static ulong ParseUInt64(string value, string source)
    {
        if (!ulong.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed))
        {
            throw Invalid($"{source} contains a non-numeric counter.");
        }

        return parsed;
    }

    private static string[] Lines(string content) => content.Split(
        ['\r', '\n'],
        StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static string[] Split(string content) => content.Split(
        (char[]?)null,
        StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static InvalidDataException Invalid(string message) => new(message);
}

internal readonly record struct CpuSample(
    ulong Total,
    ulong Idle,
    int LogicalProcessorCount);
// CoreWatch Atlas module: LinuxProcParser.
