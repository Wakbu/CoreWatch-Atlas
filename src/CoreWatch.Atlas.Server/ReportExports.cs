using System.Globalization;
using System.Reflection;
using System.Text;
using System.Text.Json;

namespace CoreWatch.Atlas.Server;

internal static class ReportExports
{
    private const double WarningThreshold = 80;
    private const double CriticalThreshold = 90;

    public static byte[] Csv(ServerReport report)
    {
        var csv = new CsvDocument();
        csv.Row("report", "field", "value");
        csv.Row("report", "target", report.HostName);
        csv.Row("report", "period_from_utc", report.FromUtc);
        csv.Row("report", "period_to_utc", report.ToUtc);
        csv.Row("report", "generated_at_utc", DateTimeOffset.UtcNow);
        csv.Row("report", "corewatch_version", InstalledVersion());
        csv.Row("kpi", "availability_percent", report.AvailabilityPercent);
        csv.Row("kpi", "active_alerts", report.Alerts.Count(x => x.State == "active"));
        csv.Row("kpi", "resolved_alerts", report.Alerts.Count(x => x.State != "active"));
        AddMetricRows(csv, "cpu", report.Cpu);
        AddMetricRows(csv, "memory", report.Memory);
        AddMetricRows(csv, "disk", report.Disk);
        csv.Row("server", "operating_system", report.OperatingSystem);
        csv.Row("server", "agent_version", report.AgentVersion);
        csv.Row("server", "last_collection_utc", report.LastCollectionUtc);
        csv.Row("server", "snapshot_count", report.SnapshotCount);
        csv.Row("server", "collection_gap_count", report.CollectionGapCount);

        csv.Blank();
        csv.Row("trend", "captured_at_utc", "cpu_percent", "memory_percent", "disk_percent");
        foreach (var point in report.TrendPoints ?? [])
            csv.Row("trend", point.CapturedAtUtc, point.CpuPercent, point.MemoryPercent, point.DiskPercent);

        csv.Blank();
        csv.Row("alerts", "id", "severity", "opened_at_utc", "resolved_at_utc", "status", "server", "rule", "metric", "current_value");
        foreach (var alert in report.Alerts.OrderByDescending(x => x.OpenedAtUtc))
            csv.Row("alerts", alert.Id, alert.Severity, alert.OpenedAtUtc, alert.ResolvedAtUtc, alert.State, alert.HostName, alert.RuleName, alert.MetricType, alert.CurrentValue);

        csv.Blank();
        csv.Row("alert_actions", "alert_id", "created_at_utc", "action", "actor", "assignee", "note");
        foreach (var pair in report.AlertActions ?? new Dictionary<long, IReadOnlyList<AlertAction>>())
            foreach (var action in pair.Value)
                csv.Row("alert_actions", pair.Key, action.CreatedAtUtc, action.ActionType, action.Actor, action.Assignee, action.Note);

        csv.Blank();
        csv.Row("capacity", "partition_id", "mount_point", "used_percent", "daily_growth_percent", "projected_days_until_full");
        foreach (var partition in report.Partitions ?? [])
            csv.Row("capacity", partition.Id, partition.MountPoint, partition.CurrentUsedPercent, partition.DailyGrowthPercent, partition.DaysUntilFull);
        return csv.Bytes();
    }

    public static byte[] Pdf(ServerReport report)
    {
        var document = new PdfDocument();
        var overview = document.Page();
        Header(overview, "SERVER OPERATIONS REPORT", report.HostName, report.FromUtc, report.ToUtc);
        Kpis(overview, 714,
            ("Availability", Percent(report.AvailabilityPercent), Accent(report.AvailabilityPercent)),
            ("Active alerts", report.Alerts.Count(x => x.State == "active").ToString(CultureInfo.InvariantCulture), "0.82 0.20 0.25"),
            ("Resolved", report.Alerts.Count(x => x.State != "active").ToString(CultureInfo.InvariantCulture), "0.15 0.62 0.47"),
            ("CPU avg / peak", Pair(report.Cpu.Average, report.Cpu.Maximum), "0.13 0.47 0.85"),
            ("Memory avg / peak", Pair(report.Memory.Average, report.Memory.Maximum), "0.51 0.31 0.86"),
            ("Disk used", Percent(report.Disk.Latest), Accent(report.Disk.Latest)));

        overview.Text(44, 550, 15, "RESOURCE TRENDS", true, "0.09 0.14 0.23");
        var points = report.TrendPoints ?? [];
        Chart(overview, 44, 386, 524, 140, points.Select(x => x.CpuPercent).ToArray(), "CPU", "0.13 0.47 0.85");
        Chart(overview, 44, 210, 524, 140, points.Select(x => x.MemoryPercent).ToArray(), "MEMORY", "0.51 0.31 0.86");
        Chart(overview, 44, 34, 524, 140, points.Select(x => x.DiskPercent).ToArray(), "DISK", "0.15 0.62 0.47");

        DetailsPage(document, report);
        AlertsPages(document, report);
        return document.Bytes();
    }

    public static byte[] FleetCsv(IReadOnlyList<AgentSummary> agents, IReadOnlyList<AlertRecord> alerts)
    {
        var csv = new CsvDocument();
        csv.Row("report", "field", "value");
        csv.Row("report", "target", "All servers");
        csv.Row("report", "generated_at_utc", DateTimeOffset.UtcNow);
        csv.Row("report", "corewatch_version", InstalledVersion());
        csv.Row("kpi", "server_count", agents.Count);
        csv.Row("kpi", "online_count", agents.Count(x => x.Online));
        csv.Row("kpi", "active_alerts", alerts.Count(x => x.State == "active"));
        csv.Row("kpi", "resolved_alerts", alerts.Count(x => x.State != "active"));
        csv.Blank();
        csv.Row("servers", "host", "os", "agent_version", "online", "last_collection_utc", "cpu_percent", "memory_percent", "disk_percent", "active_alerts");
        foreach (var agent in agents.OrderBy(x => x.HostName, StringComparer.OrdinalIgnoreCase))
        {
            var metrics = LatestMetrics(agent.LatestSnapshot?.Metrics);
            csv.Row("servers", agent.HostName, agent.OperatingSystem, agent.AgentVersion, agent.Online, agent.LastSeenAtUtc, metrics.Cpu, metrics.Memory, metrics.Disk, alerts.Count(x => x.AgentId == agent.AgentId && x.State == "active"));
        }
        csv.Blank();
        csv.Row("alerts", "id", "severity", "opened_at_utc", "resolved_at_utc", "status", "server", "rule", "metric", "current_value");
        foreach (var alert in alerts.OrderByDescending(x => x.OpenedAtUtc))
            csv.Row("alerts", alert.Id, alert.Severity, alert.OpenedAtUtc, alert.ResolvedAtUtc, alert.State, alert.HostName, alert.RuleName, alert.MetricType, alert.CurrentValue);
        return csv.Bytes();
    }

    public static byte[] FleetPdf(IReadOnlyList<AgentSummary> agents, IReadOnlyList<AlertRecord> alerts)
    {
        var now = DateTimeOffset.UtcNow;
        var document = new PdfDocument();
        var page = document.Page();
        Header(page, "FLEET DAILY OPERATIONS REPORT", "All servers", now.AddDays(-1), now);
        var availability = agents.Count == 0 ? 100 : agents.Count(x => x.Online) * 100d / agents.Count;
        var values = agents.Select(x => LatestMetrics(x.LatestSnapshot?.Metrics)).ToArray();
        Kpis(page, 714,
            ("Availability", Percent(availability), Accent(availability)),
            ("Online / total", $"{agents.Count(x => x.Online)} / {agents.Count}", "0.13 0.47 0.85"),
            ("Active alerts", alerts.Count(x => x.State == "active").ToString(CultureInfo.InvariantCulture), "0.82 0.20 0.25"),
            ("Resolved", alerts.Count(x => x.State != "active").ToString(CultureInfo.InvariantCulture), "0.15 0.62 0.47"),
            ("CPU avg / peak", Pair(Average(values.Select(x => x.Cpu)), Maximum(values.Select(x => x.Cpu))), "0.13 0.47 0.85"),
            ("Memory avg / peak", Pair(Average(values.Select(x => x.Memory)), Maximum(values.Select(x => x.Memory))), "0.51 0.31 0.86"));
        page.Text(44, 550, 15, "CURRENT FLEET RESOURCE PROFILE", true, "0.09 0.14 0.23");
        Chart(page, 44, 386, 524, 140, values.Select(x => x.Cpu).ToArray(), "CPU BY SERVER", "0.13 0.47 0.85");
        Chart(page, 44, 210, 524, 140, values.Select(x => x.Memory).ToArray(), "MEMORY BY SERVER", "0.51 0.31 0.86");
        Chart(page, 44, 34, 524, 140, values.Select(x => x.Disk).ToArray(), "DISK BY SERVER", "0.15 0.62 0.47");
        FleetDetailsPages(document, agents, alerts);
        return document.Bytes();
    }

    private static void DetailsPage(PdfDocument document, ServerReport report)
    {
        var page = document.Page();
        PageTitle(page, "SERVER DETAILS & CAPACITY", report.HostName);
        var rows = new[]
        {
            ("Operating system", Empty(report.OperatingSystem)),
            ("Agent version", Empty(report.AgentVersion)),
            ("Installed CoreWatch", InstalledVersion()),
            ("Last collection (UTC)", report.LastCollectionUtc?.ToString("u", CultureInfo.InvariantCulture) ?? "No data"),
            ("Snapshot count", report.SnapshotCount.ToString("N0", CultureInfo.InvariantCulture)),
            ("Collection gaps", report.CollectionGapCount.ToString("N0", CultureInfo.InvariantCulture)),
        };
        var y = 702d;
        foreach (var row in rows)
        {
            page.Fill(44, y - 8, 524, 34, y % 2 == 0 ? "0.96 0.97 0.99" : "1 1 1");
            page.Text(56, y + 3, 10, row.Item1, false, "0.35 0.40 0.48");
            page.Text(260, y + 3, 10, row.Item2, true, "0.09 0.14 0.23");
            y -= 38;
        }
        page.Text(44, 440, 15, "CAPACITY FORECAST", true, "0.09 0.14 0.23");
        TableHeader(page, 44, 406, new[] { ("Partition", 180d), ("Used", 85d), ("Daily growth", 110d), ("Days until full", 149d) });
        y = 376;
        foreach (var item in (report.Partitions ?? []).Take(10))
        {
            TableRow(page, 44, y, new[]
            {
                (Trim(item.MountPoint, 26), 180d), (Percent(item.CurrentUsedPercent), 85d),
                (item.DailyGrowthPercent is null ? "Not enough data" : $"{item.DailyGrowthPercent:F2}% / day", 110d),
                (item.DaysUntilFull is null ? "Not projected" : $"{item.DaysUntilFull:F0} days", 149d),
            });
            y -= 30;
        }
        if ((report.Partitions?.Count ?? 0) == 0)
            page.Text(56, 376, 10, "No partition capacity data is available for this period.", false, "0.40 0.44 0.50");
    }

    private static void AlertsPages(PdfDocument document, ServerReport report)
    {
        var alerts = report.Alerts.OrderByDescending(x => x.OpenedAtUtc).ToArray();
        if (alerts.Length == 0)
        {
            var empty = document.Page();
            PageTitle(empty, "ALERTS & ACTION HISTORY", report.HostName);
            empty.Text(44, 700, 12, "No alerts occurred during the report period.", false, "0.35 0.40 0.48");
            return;
        }

        foreach (var chunk in alerts.Chunk(12))
        {
            var page = document.Page();
            PageTitle(page, "ALERTS & ACTION HISTORY", report.HostName);
            TableHeader(page, 44, 704, new[] { ("Severity", 70d), ("Opened (UTC)", 115d), ("Status", 70d), ("Server", 100d), ("Rule / latest action", 169d) });
            var y = 674d;
            foreach (var alert in chunk)
            {
                var action = report.AlertActions is not null && report.AlertActions.TryGetValue(alert.Id, out var history) ? history.LastOrDefault() : null;
                var detail = action is null ? alert.RuleName : $"{alert.RuleName} | {action.ActionType} by {action.Actor}";
                TableRow(page, 44, y, new[]
                {
                    (alert.Severity.ToUpperInvariant(), 70d),
                    (alert.OpenedAtUtc.ToString("MM-dd HH:mm", CultureInfo.InvariantCulture), 115d),
                    (alert.State, 70d), (Trim(alert.HostName, 16), 100d), (Trim(detail, 28), 169d),
                });
                y -= 46;
                page.Text(56, y + 17, 8, $"Metric: {alert.MetricType}  Value: {alert.CurrentValue:F1}  Resolved: {alert.ResolvedAtUtc?.ToString("u", CultureInfo.InvariantCulture) ?? "-"}", false, "0.40 0.44 0.50");
            }
        }
    }

    private static void FleetDetailsPages(PdfDocument document, IReadOnlyList<AgentSummary> agents, IReadOnlyList<AlertRecord> alerts)
    {
        foreach (var chunk in agents.OrderBy(x => x.HostName, StringComparer.OrdinalIgnoreCase).Chunk(18).DefaultIfEmpty([]))
        {
            var page = document.Page();
            PageTitle(page, "FLEET SERVER DETAILS", $"{agents.Count} managed servers");
            TableHeader(page, 44, 704, new[] { ("Server", 145d), ("OS", 125d), ("Agent", 80d), ("Status", 70d), ("Last collection (UTC)", 104d) });
            var y = 674d;
            foreach (var agent in chunk)
            {
                TableRow(page, 44, y, new[]
                {
                    (Trim(agent.HostName, 22), 145d), (Trim(agent.OperatingSystem, 18), 125d),
                    (Trim(agent.AgentVersion, 12), 80d), (agent.Online ? "Online" : "Offline", 70d),
                    (agent.LastSeenAtUtc?.ToString("MM-dd HH:mm", CultureInfo.InvariantCulture) ?? "No data", 104d),
                });
                y -= 30;
            }
        }
        if (alerts.Count == 0) return;
        var report = new ServerReport(Guid.Empty, "All servers", DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow, 0, 0, new(null, null, null), new(null, null, null), new(null, null, null), alerts);
        AlertsPages(document, report);
    }

    private static void Header(PdfPage page, string title, string target, DateTimeOffset from, DateTimeOffset to)
    {
        page.Fill(0, 0, 612, 842, "0.97 0.98 1");
        page.Fill(0, 754, 612, 88, "0.05 0.10 0.19");
        page.Fill(0, 754, 8, 88, "0.16 0.73 0.71");
        page.Text(44, 810, 19, title, true, "1 1 1");
        page.Text(44, 785, 10, $"Target: {Trim(target, 60)}", false, "0.76 0.82 0.91");
        page.Text(300, 785, 9, $"Period: {from:u} - {to:u}", false, "0.76 0.82 0.91");
        page.Text(300, 768, 9, $"Generated: {DateTimeOffset.UtcNow:u}  |  CoreWatch {InstalledVersion()}", false, "0.76 0.82 0.91");
    }

    private static void PageTitle(PdfPage page, string title, string subtitle)
    {
        page.Fill(0, 0, 612, 842, "0.97 0.98 1");
        page.Fill(0, 772, 612, 70, "0.05 0.10 0.19");
        page.Fill(0, 772, 8, 70, "0.16 0.73 0.71");
        page.Text(44, 813, 18, title, true, "1 1 1");
        page.Text(44, 790, 9, Trim(subtitle, 70), false, "0.76 0.82 0.91");
    }

    private static void Kpis(PdfPage page, double top, params (string Label, string Value, string Color)[] cards)
    {
        for (var i = 0; i < cards.Length; i++)
        {
            var col = i % 3;
            var row = i / 3;
            var x = 44 + col * 176;
            var y = top - row * 78;
            page.Fill(x, y - 58, 164, 64, "1 1 1");
            page.Fill(x, y - 58, 5, 64, cards[i].Color);
            page.Text(x + 16, y - 15, 8, cards[i].Label.ToUpperInvariant(), true, "0.40 0.44 0.50");
            page.Text(x + 16, y - 42, 16, cards[i].Value, true, "0.09 0.14 0.23");
        }
    }

    private static void Chart(PdfPage page, double x, double y, double width, double height, IReadOnlyList<double?> values, string label, string color)
    {
        page.Fill(x, y, width, height, "1 1 1");
        page.Text(x + 12, y + height - 20, 9, label, true, "0.25 0.30 0.38");
        var plotX = x + 42;
        var plotY = y + 18;
        var plotWidth = width - 56;
        var plotHeight = height - 48;
        for (var level = 0; level <= 100; level += 25)
        {
            var lineY = plotY + plotHeight * level / 100d;
            page.Line(plotX, lineY, plotX + plotWidth, lineY, "0.88 0.90 0.93", 0.5);
            page.Text(x + 10, lineY - 3, 7, level.ToString(CultureInfo.InvariantCulture), false, "0.55 0.59 0.65");
        }
        Threshold(page, plotX, plotY, plotWidth, plotHeight, WarningThreshold, "0.93 0.58 0.16");
        Threshold(page, plotX, plotY, plotWidth, plotHeight, CriticalThreshold, "0.82 0.20 0.25");
        var samples = Downsample(values, 120);
        var prior = (X: 0d, Y: 0d, Has: false);
        for (var i = 0; i < samples.Count; i++)
        {
            if (samples[i] is not { } value) { prior.Has = false; continue; }
            var px = plotX + (samples.Count <= 1 ? 0 : plotWidth * i / (samples.Count - 1));
            var py = plotY + plotHeight * Math.Clamp(value, 0, 100) / 100;
            if (prior.Has) page.Line(prior.X, prior.Y, px, py, color, 1.6);
            prior = (px, py, true);
        }
        if (samples.Count == 0) page.Text(plotX + 150, plotY + 35, 9, "No observations", false, "0.55 0.59 0.65");
    }

    private static void Threshold(PdfPage page, double x, double y, double width, double height, double threshold, string color)
    {
        var lineY = y + height * threshold / 100;
        page.Line(x, lineY, x + width, lineY, color, 0.8, true);
    }

    private static IReadOnlyList<double?> Downsample(IReadOnlyList<double?> values, int maximum)
    {
        if (values.Count <= maximum) return values;
        var result = new double?[maximum];
        for (var i = 0; i < maximum; i++) result[i] = values[(int)Math.Round(i * (values.Count - 1d) / (maximum - 1))];
        return result;
    }

    private static void TableHeader(PdfPage page, double x, double y, IReadOnlyList<(string Text, double Width)> cells)
    {
        page.Fill(x, y, cells.Sum(c => c.Width), 26, "0.09 0.14 0.23");
        var cursor = x;
        foreach (var cell in cells)
        {
            page.Text(cursor + 8, y + 9, 8, cell.Text.ToUpperInvariant(), true, "1 1 1");
            cursor += cell.Width;
        }
    }

    private static void TableRow(PdfPage page, double x, double y, IReadOnlyList<(string Text, double Width)> cells)
    {
        page.Fill(x, y, cells.Sum(c => c.Width), 28, "1 1 1");
        var cursor = x;
        foreach (var cell in cells)
        {
            page.Text(cursor + 8, y + 10, 8, cell.Text, false, "0.18 0.23 0.31");
            cursor += cell.Width;
        }
    }

    private static void AddMetricRows(CsvDocument csv, string name, MetricReport metric)
    {
        csv.Row("kpi", $"{name}_average_percent", metric.Average);
        csv.Row("kpi", $"{name}_peak_percent", metric.Maximum);
        csv.Row("kpi", $"{name}_latest_percent", metric.Latest);
    }

    private static (double? Cpu, double? Memory, double? Disk) LatestMetrics(JsonElement? metrics)
    {
        if (metrics is null) return (null, null, null);
        try
        {
            var value = metrics.Value;
            var cpu = value.GetProperty("cpu").GetProperty("usageRatio").GetDouble() * 100;
            var memoryNode = value.GetProperty("memory");
            var memory = 100 * (1 - memoryNode.GetProperty("availableBytes").GetDouble() / memoryNode.GetProperty("totalBytes").GetDouble());
            var disk = value.GetProperty("fileSystems").EnumerateArray().Select(x => (Total: x.GetProperty("totalBytes").GetDouble(), Free: x.GetProperty("availableBytes").GetDouble())).Aggregate((0d, 0d), (a, b) => (a.Item1 + b.Total, a.Item2 + b.Free));
            return (cpu, memory, disk.Item1 > 0 ? 100 * (1 - disk.Item2 / disk.Item1) : null);
        }
        catch (KeyNotFoundException) { return (null, null, null); }
        catch (InvalidOperationException) { return (null, null, null); }
    }

    private static double? Average(IEnumerable<double?> values)
    {
        var materialized = values.Where(x => x.HasValue).Select(x => x!.Value).ToArray();
        return materialized.Length == 0 ? null : materialized.Average();
    }

    private static double? Maximum(IEnumerable<double?> values)
    {
        var materialized = values.Where(x => x.HasValue).Select(x => x!.Value).ToArray();
        return materialized.Length == 0 ? null : materialized.Max();
    }

    private static string InstalledVersion() => typeof(ReportExports).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0] ?? typeof(ReportExports).Assembly.GetName().Version?.ToString() ?? "unknown";
    private static string Percent(double? value) => value is null ? "No data" : $"{value:F1}%";
    private static string Pair(double? average, double? maximum) => average is null ? "No data" : $"{average:F1}% / {maximum:F1}%";
    private static string Accent(double? value) => value >= CriticalThreshold ? "0.82 0.20 0.25" : value >= WarningThreshold ? "0.93 0.58 0.16" : "0.15 0.62 0.47";
    private static string Empty(string value) => string.IsNullOrWhiteSpace(value) ? "Not reported" : value;
    private static string Trim(string value, int length) => value.Length <= length ? value : value[..Math.Max(0, length - 3)] + "...";

    private sealed class CsvDocument
    {
        private readonly StringBuilder builder = new();

        public void Blank() => builder.Append("\r\n");

        public void Row(params object?[] values)
        {
            builder.AppendJoin(',', values.Select(Format));
            builder.Append("\r\n");
        }

        public byte[] Bytes()
        {
            var encoding = new UTF8Encoding(true);
            var body = encoding.GetBytes(builder.ToString());
            return [.. encoding.GetPreamble(), .. body];
        }

        private static string Format(object? value)
        {
            var text = value switch
            {
                null => "",
                DateTimeOffset timestamp => timestamp.ToString("O", CultureInfo.InvariantCulture),
                IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
                _ => value.ToString() ?? "",
            };
            return '"' + text.Replace("\"", "\"\"") + '"';
        }
    }

    private sealed class PdfDocument
    {
        private readonly List<PdfPage> pages = [];

        public PdfPage Page()
        {
            var page = new PdfPage();
            pages.Add(page);
            return page;
        }

        public byte[] Bytes()
        {
            var objects = new List<string>();
            var pageObjectNumbers = new List<int>();
            for (var i = 0; i < pages.Count; i++) pageObjectNumbers.Add(3 + i * 2);
            objects.Add("<< /Type /Catalog /Pages 2 0 R >>");
            objects.Add($"<< /Type /Pages /Kids [{string.Join(' ', pageObjectNumbers.Select(x => $"{x} 0 R"))}] /Count {pages.Count} >>");
            for (var i = 0; i < pages.Count; i++)
            {
                var contentObject = pageObjectNumbers[i] + 1;
                objects.Add($"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 842] /Resources << /Font << /F1 {3 + pages.Count * 2} 0 R /F2 {4 + pages.Count * 2} 0 R >> >> /Contents {contentObject} 0 R >>");
                var content = pages[i].Content;
                objects.Add($"<< /Length {Encoding.Latin1.GetByteCount(content)} >>\nstream\n{content}\nendstream");
            }
            objects.Add("<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding >>");
            objects.Add("<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica-Bold /Encoding /WinAnsiEncoding >>");

            var output = new StringBuilder("%PDF-1.4\n%CoreWatch Atlas operations report\n");
            var offsets = new List<int> { 0 };
            for (var i = 0; i < objects.Count; i++)
            {
                offsets.Add(Encoding.Latin1.GetByteCount(output.ToString()));
                output.Append(i + 1).Append(" 0 obj\n").Append(objects[i]).Append("\nendobj\n");
            }
            var xref = Encoding.Latin1.GetByteCount(output.ToString());
            output.Append("xref\n0 ").Append(objects.Count + 1).Append("\n0000000000 65535 f \n");
            foreach (var offset in offsets.Skip(1)) output.Append(offset.ToString("D10", CultureInfo.InvariantCulture)).Append(" 00000 n \n");
            output.Append("trailer << /Size ").Append(objects.Count + 1).Append(" /Root 1 0 R >>\nstartxref\n").Append(xref).Append("\n%%EOF");
            return Encoding.Latin1.GetBytes(output.ToString());
        }
    }

    private sealed class PdfPage
    {
        private readonly StringBuilder content = new();
        public string Content => content.ToString();

        public void Fill(double x, double y, double width, double height, string color) => content.Append(color).Append(" rg ").Append(Number(x)).Append(' ').Append(Number(y)).Append(' ').Append(Number(width)).Append(' ').Append(Number(height)).Append(" re f\n");

        public void Text(double x, double y, double size, string value, bool bold, string color)
        {
            content.Append("BT ").Append(color).Append(" rg /").Append(bold ? "F2" : "F1").Append(' ').Append(Number(size)).Append(" Tf ").Append(Number(x)).Append(' ').Append(Number(y)).Append(" Td (").Append(Escape(value)).Append(") Tj ET\n");
        }

        public void Line(double x1, double y1, double x2, double y2, string color, double width, bool dashed = false)
        {
            content.Append(color).Append(" RG ").Append(Number(width)).Append(" w ");
            if (dashed) content.Append("[4 3] 0 d ");
            content.Append(Number(x1)).Append(' ').Append(Number(y1)).Append(" m ").Append(Number(x2)).Append(' ').Append(Number(y2)).Append(" l S ");
            if (dashed) content.Append("[] 0 d");
            content.Append('\n');
        }

        private static string Escape(string value)
        {
            var safe = new StringBuilder(value.Length);
            foreach (var character in value)
            {
                if (character is '\\' or '(' or ')') safe.Append('\\').Append(character);
                else if (character is >= ' ' and <= '~') safe.Append(character);
                else safe.Append('?');
            }
            return safe.ToString();
        }

        private static string Number(double value) => value.ToString("0.##", CultureInfo.InvariantCulture);
    }
}

// CoreWatch Atlas module: ReportExports.
