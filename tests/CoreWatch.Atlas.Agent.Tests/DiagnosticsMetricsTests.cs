using CoreWatch.Atlas.Contracts;

namespace CoreWatch.Atlas.Agent.Tests;

[TestClass]
public sealed class DiagnosticsMetricsTests
{
    [TestMethod]
    public void PrometheusOutputIncludesServiceAndDiagnosticHealth()
    {
        var snapshot=new SystemMetricsSnapshot(DateTimeOffset.UtcNow,TimeSpan.FromMinutes(1),new AgentIdentity("agent-test","host","Linux","x64","1.0.0"),new CpuMetrics(.25,4),new MemoryMetrics(1000,500),[],[],[],[new MonitoredServiceMetrics("nginx","active")],[new DiagnosticCheckMetrics("tcp:db:5432","port","open","2 ms")]);
        var output=PrometheusMetricsFormatter.Format(snapshot);
        StringAssert.Contains(output,"corewatch_atlas_service_healthy{service=\"nginx\",status=\"active\"} 1");
        StringAssert.Contains(output,"corewatch_atlas_diagnostic_healthy{id=\"tcp:db:5432\",kind=\"port\",status=\"open\"} 1");
    }
}
// CoreWatch Atlas module: DiagnosticsMetricsTests.
