using System.Diagnostics;
using CoreWatch.Atlas.Contracts;

namespace CoreWatch.Atlas.Agent;

internal static class ServiceDiagnostics
{
    public static async Task<IReadOnlyList<MonitoredServiceMetrics>> ReadAsync(IEnumerable<string> configured, CancellationToken token)
    {
        var names=configured.Where(x=>!string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).Take(50).ToArray();
        var result=new List<MonitoredServiceMetrics>();
        foreach(var name in names)
        {
            // systemctl/sc의 실패는 Agent 전체 수집 실패가 아니라 해당 서비스의 unknown 상태다.
            var file=OperatingSystem.IsWindows()?"sc.exe":"systemctl";
            var args=OperatingSystem.IsWindows()?$"query \"{name}\"":"is-active "+name;
            try { using var p=Process.Start(new ProcessStartInfo(file,args){RedirectStandardOutput=true,UseShellExecute=false,CreateNoWindow=true})!; var output=await p.StandardOutput.ReadToEndAsync(token);await p.WaitForExitAsync(token);result.Add(new(name,p.ExitCode==0?(OperatingSystem.IsWindows()&&output.Contains("RUNNING",StringComparison.OrdinalIgnoreCase)?"running":"active"):"inactive")); }
            catch { result.Add(new(name,"unknown")); }
        }
        return result;
    }
}
// CoreWatch Atlas module: ServiceDiagnostics.
