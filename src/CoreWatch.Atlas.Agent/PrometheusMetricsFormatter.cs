using System.Globalization;
using System.Text;
using CoreWatch.Atlas.Contracts;

namespace CoreWatch.Atlas.Agent;

public static class PrometheusMetricsFormatter
{
    public static string Format(SystemMetricsSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var builder = new StringBuilder(2048);

        AppendGauge(builder, "corewatch_atlas_cpu_usage_ratio",
            "Current average CPU usage ratio.", snapshot.Cpu.UsageRatio);
        AppendGauge(builder, "corewatch_atlas_cpu_logical_processors",
            "Logical processors visible to the agent.",
            snapshot.Cpu.LogicalProcessorCount);
        AppendGauge(builder, "corewatch_atlas_memory_total_bytes",
            "Total physical memory in bytes.", snapshot.Memory.TotalBytes);
        AppendGauge(builder, "corewatch_atlas_memory_available_bytes",
            "Available physical memory in bytes.", snapshot.Memory.AvailableBytes);
        AppendGauge(builder, "corewatch_atlas_uptime_seconds",
            "Operating system uptime in seconds.", snapshot.Uptime.TotalSeconds);
        AppendGauge(builder, "corewatch_atlas_snapshot_timestamp_seconds",
            "UTC Unix timestamp of the latest snapshot.",
            snapshot.CapturedAtUtc.ToUnixTimeMilliseconds() / 1000d);

        AppendInfo(builder, snapshot.Agent);
        AppendFileSystems(builder, snapshot.FileSystems);
        AppendDisks(builder, snapshot.Disks);
        AppendNetwork(builder, snapshot.NetworkInterfaces);
        return builder.ToString();
    }

    private static void AppendInfo(StringBuilder builder, AgentIdentity agent)
    {
        AppendHeader(builder, "corewatch_atlas_agent_info", "gauge",
            "Static identity information for this agent.");
        builder.Append("corewatch_atlas_agent_info{agent_id=\"")
            .Append(Escape(agent.AgentId))
            .Append("\",host=\"")
            .Append(Escape(agent.HostName))
            .Append("\",os=\"")
            .Append(Escape(agent.OperatingSystem))
            .Append("\",architecture=\"")
            .Append(Escape(agent.Architecture))
            .Append("\",version=\"")
            .Append(Escape(agent.AgentVersion))
            .Append("\"} 1\n");
    }

    private static void AppendFileSystems(
        StringBuilder builder,
        IReadOnlyList<FileSystemMetrics> fileSystems)
    {
        AppendHeader(builder, "corewatch_atlas_filesystem_total_bytes", "gauge",
            "Total filesystem capacity in bytes.");
        AppendHeader(builder, "corewatch_atlas_filesystem_available_bytes", "gauge",
            "Available filesystem capacity in bytes.");
        foreach (var fileSystem in fileSystems)
        {
            var labels = $"{{id=\"{Escape(fileSystem.Id)}\",mount=\"{Escape(fileSystem.MountPoint)}\"}}";
            AppendSample(builder, "corewatch_atlas_filesystem_total_bytes",
                labels, fileSystem.TotalBytes);
            AppendSample(builder, "corewatch_atlas_filesystem_available_bytes",
                labels, fileSystem.AvailableBytes);
        }
    }

    private static void AppendDisks(
        StringBuilder builder,
        IReadOnlyList<DiskIoMetrics> disks)
    {
        AppendHeader(builder, "corewatch_atlas_disk_read_bytes_total", "counter",
            "Cumulative bytes read from a disk.");
        AppendHeader(builder, "corewatch_atlas_disk_write_bytes_total", "counter",
            "Cumulative bytes written to a disk.");
        foreach (var disk in disks)
        {
            var labels = $"{{device=\"{Escape(disk.Device)}\"}}";
            AppendSample(builder, "corewatch_atlas_disk_read_bytes_total",
                labels, disk.ReadBytesTotal);
            AppendSample(builder, "corewatch_atlas_disk_write_bytes_total",
                labels, disk.WriteBytesTotal);
        }
    }

    private static void AppendNetwork(
        StringBuilder builder,
        IReadOnlyList<NetworkInterfaceMetrics> interfaces)
    {
        AppendHeader(builder, "corewatch_atlas_network_receive_bytes_total", "counter",
            "Cumulative bytes received by an interface.");
        AppendHeader(builder, "corewatch_atlas_network_transmit_bytes_total", "counter",
            "Cumulative bytes transmitted by an interface.");
        foreach (var networkInterface in interfaces)
        {
            var labels = $"{{device=\"{Escape(networkInterface.Name)}\"}}";
            AppendSample(builder, "corewatch_atlas_network_receive_bytes_total",
                labels, networkInterface.ReceiveBytesTotal);
            AppendSample(builder, "corewatch_atlas_network_transmit_bytes_total",
                labels, networkInterface.TransmitBytesTotal);
        }
    }

    private static void AppendGauge(
        StringBuilder builder,
        string name,
        string help,
        double value)
    {
        AppendHeader(builder, name, "gauge", help);
        AppendSample(builder, name, string.Empty, value);
    }

    private static void AppendGauge(
        StringBuilder builder,
        string name,
        string help,
        ulong value)
    {
        AppendHeader(builder, name, "gauge", help);
        AppendSample(builder, name, string.Empty, value);
    }

    private static void AppendGauge(
        StringBuilder builder,
        string name,
        string help,
        int value)
    {
        AppendHeader(builder, name, "gauge", help);
        AppendSample(builder, name, string.Empty, value);
    }

    private static void AppendHeader(
        StringBuilder builder,
        string name,
        string type,
        string help)
    {
        builder.Append("# HELP ").Append(name).Append(' ').Append(help).Append('\n');
        builder.Append("# TYPE ").Append(name).Append(' ').Append(type).Append('\n');
    }

    private static void AppendSample(
        StringBuilder builder,
        string name,
        string labels,
        double value)
    {
        builder.Append(name)
            .Append(labels)
            .Append(' ')
            .Append(value.ToString("G17", CultureInfo.InvariantCulture))
            .Append('\n');
    }

    private static void AppendSample(
        StringBuilder builder,
        string name,
        string labels,
        ulong value)
    {
        builder.Append(name)
            .Append(labels)
            .Append(' ')
            .Append(value.ToString(CultureInfo.InvariantCulture))
            .Append('\n');
    }

    private static void AppendSample(
        StringBuilder builder,
        string name,
        string labels,
        int value)
    {
        builder.Append(name)
            .Append(labels)
            .Append(' ')
            .Append(value.ToString(CultureInfo.InvariantCulture))
            .Append('\n');
    }

    private static string Escape(string value) => value
        .Replace("\\", "\\\\", StringComparison.Ordinal)
        .Replace("\n", "\\n", StringComparison.Ordinal)
        .Replace("\"", "\\\"", StringComparison.Ordinal);
}
