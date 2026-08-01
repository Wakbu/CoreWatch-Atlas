using System.Globalization;
using System.Text;

namespace CoreWatch.Atlas.Server;

internal static class ReportExports
{
    public static byte[] Csv(ServerReport r)
    {
        var b=new StringBuilder("metric,value\r\n");
        void Add(string key,object? value)=>b.Append(key).Append(',').Append(Convert.ToString(value,CultureInfo.InvariantCulture)).Append("\r\n");
        Add("host",Quote(r.HostName));Add("from_utc",r.FromUtc);Add("to_utc",r.ToUtc);Add("snapshots",r.SnapshotCount);Add("availability_percent",r.AvailabilityPercent);Add("cpu_average_percent",r.Cpu.Average);Add("cpu_maximum_percent",r.Cpu.Maximum);Add("memory_average_percent",r.Memory.Average);Add("memory_maximum_percent",r.Memory.Maximum);Add("disk_latest_percent",r.Disk.Latest);Add("alerts",r.Alerts.Count);
        return new UTF8Encoding(true).GetBytes(b.ToString());
    }
    public static byte[] Pdf(ServerReport r)
    {
        var lines=new[]{"CoreWatch Atlas Server Report",$"Host: {Ascii(r.HostName)}",$"Period: {r.FromUtc:u} - {r.ToUtc:u}",$"Availability: {r.AvailabilityPercent:F1}%",$"CPU average / max: {r.Cpu.Average:F1}% / {r.Cpu.Maximum:F1}%",$"Memory average / max: {r.Memory.Average:F1}% / {r.Memory.Maximum:F1}%",$"Disk latest: {r.Disk.Latest:F1}%",$"Snapshots: {r.SnapshotCount}   Alerts: {r.Alerts.Count}"};
        var content="BT /F1 12 Tf 50 790 Td "+string.Join(" Tj 0 -24 Td ",lines.Select(x=>$"({Escape(x)})"))+" Tj ET";var objects=new[]{"<< /Type /Catalog /Pages 2 0 R >>","<< /Type /Pages /Kids [3 0 R] /Count 1 >>","<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 842] /Resources << /Font << /F1 5 0 R >> >> /Contents 4 0 R >>",$"<< /Length {Encoding.ASCII.GetByteCount(content)} >>\nstream\n{content}\nendstream","<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>"};var b=new StringBuilder("%PDF-1.4\n");var offsets=new List<int>{0};for(var i=0;i<objects.Length;i++){offsets.Add(Encoding.ASCII.GetByteCount(b.ToString()));b.Append(i+1).Append(" 0 obj\n").Append(objects[i]).Append("\nendobj\n");}var xref=Encoding.ASCII.GetByteCount(b.ToString());b.Append("xref\n0 ").Append(objects.Length+1).Append("\n0000000000 65535 f \n");for(var i=1;i<offsets.Count;i++)b.Append(offsets[i].ToString("D10",CultureInfo.InvariantCulture)).Append(" 00000 n \n");b.Append("trailer << /Size ").Append(objects.Length+1).Append(" /Root 1 0 R >>\nstartxref\n").Append(xref).Append("\n%%EOF");return Encoding.ASCII.GetBytes(b.ToString());
    }
    public static byte[] FleetCsv(IReadOnlyList<AgentSummary> agents,IReadOnlyList<AlertRecord> alerts)
    { var b=new StringBuilder("host,os,agent_version,online,active_alerts\r\n");foreach(var a in agents)b.Append(Quote(a.HostName)).Append(',').Append(Quote(a.OperatingSystem)).Append(',').Append(Quote(a.AgentVersion)).Append(',').Append(a.Online).Append(',').Append(alerts.Count(x=>x.AgentId==a.AgentId&&x.State=="active")).Append("\r\n");return new UTF8Encoding(true).GetBytes(b.ToString()); }
    public static byte[] FleetPdf(IReadOnlyList<AgentSummary> agents,IReadOnlyList<AlertRecord> alerts)
    { var now=DateTimeOffset.UtcNow;var report=new ServerReport(Guid.Empty,"All servers",now.AddDays(-1),now,agents.Sum(x=>x.LatestSnapshot is null?0:1),agents.Count==0?100:agents.Count(x=>x.Online)*100d/agents.Count,new(null,null,null),new(null,null,null),new(null,null,null),alerts);return Pdf(report); }
    private static string Quote(string x)=>'"'+x.Replace("\"","\"\"")+'"';
    private static string Escape(string x)=>x.Replace("\\","\\\\").Replace("(","\\(").Replace(")","\\)");
    private static string Ascii(string x)=>new(x.Select(c=>c<=127?c:'?').ToArray());
}
