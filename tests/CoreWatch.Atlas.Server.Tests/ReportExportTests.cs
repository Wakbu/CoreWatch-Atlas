using System.Net.Mail;
using System.Text;
using System.Text.Json;

namespace CoreWatch.Atlas.Server.Tests;

[TestClass]
public sealed class ReportExportTests
{
    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    public void PdfContainsOperationsSectionsAndValidPageTree()
    {
        var text = Encoding.Latin1.GetString(ReportExports.Pdf(CreateReport()));

        StringAssert.StartsWith(text, "%PDF-1.4");
        StringAssert.Contains(text, "/Type /Pages");
        StringAssert.Contains(text, "/Count 3");
        StringAssert.Contains(text, "SERVER OPERATIONS REPORT");
        StringAssert.Contains(text, "RESOURCE TRENDS");
        StringAssert.Contains(text, "SERVER DETAILS & CAPACITY");
        StringAssert.Contains(text, "ALERTS & ACTION HISTORY");
        StringAssert.Contains(text, "CoreWatch 1.1.4");
        StringAssert.EndsWith(text, "%%EOF");
    }

    [TestMethod]
    public void PdfEscapesHostTextAndDrawsThresholds()
    {
        var report = CreateReport() with { HostName = @"db(01)\primary" };
        var text = Encoding.Latin1.GetString(ReportExports.Pdf(report));

        StringAssert.Contains(text, @"db\(01\)\\primary");
        StringAssert.Contains(text, "[4 3] 0 d");
    }

    [TestMethod]
    public void CsvHasBomAndEquivalentReadableSections()
    {
        var bytes = ReportExports.Csv(CreateReport());
        CollectionAssert.AreEqual(new byte[] { 0xEF, 0xBB, 0xBF }, bytes[..3]);
        var text = Encoding.UTF8.GetString(bytes);

        StringAssert.Contains(text, "\"trend\",\"captured_at_utc\"");
        StringAssert.Contains(text, "\"alerts\",\"id\"");
        StringAssert.Contains(text, "\"alert_actions\",\"alert_id\"");
        StringAssert.Contains(text, "\"capacity\",\"partition_id\"");
        StringAssert.Contains(text, "\"server\",\"operating_system\",\"Ubuntu 24.04\"");
    }

    [TestMethod]
    public void FleetExportsUseSameVisualSystemAndCanBeAttached()
    {
        var now = DateTimeOffset.UtcNow;
        var agent = new AgentSummary(Guid.NewGuid(), "web-01", "Linux", "x64", "1.1.4", now.AddDays(-10), now, false, null, true, new SnapshotRecord(1, now, now, Metrics()));
        var pdf = ReportExports.FleetPdf([agent], []);
        var csv = ReportExports.FleetCsv([agent], []);

        var pdfText = Encoding.Latin1.GetString(pdf);
        StringAssert.Contains(pdfText, "FLEET DAILY OPERATIONS REPORT");
        StringAssert.Contains(pdfText, "CURRENT FLEET RESOURCE PROFILE");
        StringAssert.Contains(pdfText, "FLEET SERVER DETAILS");
        Assert.AreEqual(0xEF, csv[0]);

        using var message = new MailMessage("atlas@example.test", "ops@example.test");
        message.Attachments.Add(new Attachment(new MemoryStream(pdf), "daily.pdf", "application/pdf"));
        message.Attachments.Add(new Attachment(new MemoryStream(csv), "daily.csv", "text/csv"));
        Assert.AreEqual(2, message.Attachments.Count);
        Assert.AreEqual("application/pdf", message.Attachments[0].ContentType.MediaType);
        Assert.AreEqual("text/csv", message.Attachments[1].ContentType.MediaType);
    }

    [TestMethod]
    public void GeneratedSamplesAreAvailableForVisualQa()
    {
        var now = DateTimeOffset.UtcNow;
        var report = CreateReport();
        var agent = new AgentSummary(report.AgentId, report.HostName, report.OperatingSystem, "x64", report.AgentVersion, now.AddDays(-30), now, false, null, true, new SnapshotRecord(1, now, now, Metrics()));
        var output = Environment.GetEnvironmentVariable("COREWATCH_REPORT_QA_DIR")
            ?? Path.Combine(TestContext.TestRunDirectory ?? throw new InvalidOperationException("Test run directory is unavailable."), "report-qa");
        Directory.CreateDirectory(output);
        File.WriteAllBytes(Path.Combine(output, "server-report.pdf"), ReportExports.Pdf(report));
        File.WriteAllBytes(Path.Combine(output, "fleet-report.pdf"), ReportExports.FleetPdf([agent], report.Alerts));
        TestContext.WriteLine(output);
    }

    private static ServerReport CreateReport()
    {
        var now = DateTimeOffset.UtcNow;
        var alert = new AlertRecord(7, Guid.NewGuid(), "db-01", 2, "High CPU", "cpu", "critical", 96, "resolved", now.AddHours(-2), now.AddHours(-1), now.AddMinutes(-90), "operator");
        var actions = new Dictionary<long, IReadOnlyList<AlertAction>>
        {
            [7] = [new AlertAction(1, 7, "resolved", "operator", "Scaled workload", "platform", now.AddHours(-1))],
        };
        return new ServerReport(
            alert.AgentId, "db-01", now.AddDays(-1), now, 120, 99.5,
            new MetricReport(35, 96, 42), new MetricReport(58, 82, 61), new MetricReport(72, 88, 75), [alert],
            "Ubuntu 24.04", "1.1.4", now.AddSeconds(-10), 2,
            [new ReportTrendPoint(now.AddHours(-2), 25, 55, 73), new ReportTrendPoint(now.AddHours(-1), 96, 82, 74), new ReportTrendPoint(now, 42, 61, 75)],
            [new PartitionCapacityForecast("root", "/", 75, 0.5, 50)], actions);
    }

    private static JsonElement Metrics()
    {
        using var document = JsonDocument.Parse("""
            {"cpu":{"usageRatio":0.25},"memory":{"totalBytes":1000,"availableBytes":400},"fileSystems":[{"totalBytes":1000,"availableBytes":300}]}
            """);
        return document.RootElement.Clone();
    }
}

// CoreWatch Atlas module: ReportExportTests.
