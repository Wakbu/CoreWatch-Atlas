using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using CoreWatch.Atlas.Contracts;
using Microsoft.Win32;

namespace CoreWatch.Atlas.Agent;

internal static class DiagnosticChecks
{
    public static async Task<IReadOnlyList<DiagnosticCheckMetrics>> RunAsync(DiagnosticsOptions options, CancellationToken token)
    {
        var result = new List<DiagnosticCheckMetrics>();
        foreach (var name in Clean(options.Processes, 50))
            result.Add(new("process:"+name,"process",Process.GetProcessesByName(name).Length>0?"running":"missing"));
        foreach (var name in Clean(options.Containers, 50)) result.Add(await DockerAsync(name,token));
        foreach (var endpoint in Clean(options.TcpEndpoints, 50)) result.Add(await TcpAsync(endpoint,token));
        foreach (var target in Clean(options.PingTargets, 20)) result.Add(await PingAsync(target));
        foreach (var path in Clean(options.BackupPaths, 20)) result.Add(Backup(path));
        result.Add(RebootRequired());
        result.Add(UpdateStatus());
        using var client = new HttpClient { Timeout=TimeSpan.FromSeconds(10) };
        foreach(var url in Clean(options.Urls,20).Where(x=>Uri.TryCreate(x,UriKind.Absolute,out var u)&&u.Scheme==Uri.UriSchemeHttps))
        { try { var sw=Stopwatch.StartNew(); using var response=await client.GetAsync(url,token); result.Add(new("url:"+url,"url",response.IsSuccessStatusCode?"healthy":"unhealthy",$"HTTP {(int)response.StatusCode}; {sw.ElapsedMilliseconds} ms")); } catch(Exception e) when(e is HttpRequestException or TaskCanceledException){result.Add(new("url:"+url,"url","unreachable"));} }
        return result;
    }

    private static IEnumerable<string> Clean(IEnumerable<string> source,int limit)=>source.Where(x=>!string.IsNullOrWhiteSpace(x)).Select(x=>x.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).Take(limit);
    private static async Task<DiagnosticCheckMetrics> DockerAsync(string name,CancellationToken token)
    {
        try { var start=new ProcessStartInfo("docker"){RedirectStandardOutput=true,RedirectStandardError=true,UseShellExecute=false,CreateNoWindow=true}; start.ArgumentList.Add("inspect");start.ArgumentList.Add("-f");start.ArgumentList.Add("{{.State.Status}}|{{.State.Health.Status}}|{{.RestartCount}}");start.ArgumentList.Add(name);using var p=Process.Start(start)!;var output=(await p.StandardOutput.ReadToEndAsync(token)).Trim();await p.WaitForExitAsync(token);var state=output.Split('|')[0];if(p.ExitCode!=0)return new("container:"+name,"container","missing");var detail=output;if(state=="running"){var stats=new ProcessStartInfo("docker"){RedirectStandardOutput=true,UseShellExecute=false,CreateNoWindow=true};stats.ArgumentList.Add("stats");stats.ArgumentList.Add("--no-stream");stats.ArgumentList.Add("--format");stats.ArgumentList.Add("{{.CPUPerc}} CPU | {{.MemUsage}} memory");stats.ArgumentList.Add(name);using var sp=Process.Start(stats)!;var usage=(await sp.StandardOutput.ReadToEndAsync(token)).Trim();await sp.WaitForExitAsync(token);if(sp.ExitCode==0&&!string.IsNullOrWhiteSpace(usage))detail+="; "+usage;}return new("container:"+name,"container",state,detail); } catch { return new("container:"+name,"container","unavailable"); }
    }
    private static async Task<DiagnosticCheckMetrics> TcpAsync(string endpoint,CancellationToken token)
    {
        var parts=endpoint.Split(':',2); if(parts.Length!=2||!int.TryParse(parts[1],out var port)||port is <1 or >65535)return new("tcp:"+endpoint,"port","invalid");
        try { using var tcp=new TcpClient();using var timeout=CancellationTokenSource.CreateLinkedTokenSource(token);timeout.CancelAfter(TimeSpan.FromSeconds(5));var sw=Stopwatch.StartNew();await tcp.ConnectAsync(parts[0],port,timeout.Token);return new("tcp:"+endpoint,"port","open",$"{sw.ElapsedMilliseconds} ms"); } catch { return new("tcp:"+endpoint,"port","closed"); }
    }
    private static async Task<DiagnosticCheckMetrics> PingAsync(string target)
    {
        try { using var ping=new Ping();var times=new List<long>();for(var i=0;i<3;i++){var reply=await ping.SendPingAsync(target,2000);if(reply.Status==IPStatus.Success)times.Add(reply.RoundtripTime);}return new("ping:"+target,"network",times.Count==3?"healthy":times.Count>0?"degraded":"unreachable",$"loss {100-(times.Count*100/3)}%; avg {(times.Count==0?0:times.Average()):F1} ms"); } catch { return new("ping:"+target,"network","unreachable"); }
    }
    private static DiagnosticCheckMetrics Backup(string path)
    {
        try { DateTime last;if(File.Exists(path))last=File.GetLastWriteTimeUtc(path);else if(Directory.Exists(path)){var files=Directory.EnumerateFiles(path).Take(1000).ToArray();last=files.Length==0?Directory.GetLastWriteTimeUtc(path):files.Max(File.GetLastWriteTimeUtc);}else return new("backup:"+path,"backup","missing");var age=DateTime.UtcNow-last;return new("backup:"+path,"backup",age<=TimeSpan.FromDays(1)?"current":"stale",$"last {new DateTimeOffset(last,TimeSpan.Zero):O}"); } catch { return new("backup:"+path,"backup","unavailable"); }
    }
    private static DiagnosticCheckMetrics RebootRequired()
    {
        try { var required=OperatingSystem.IsWindows()?Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\WindowsUpdate\Auto Update\RebootRequired") is not null:File.Exists("/var/run/reboot-required");return new("os:reboot-required","os",required?"required":"not-required"); } catch { return new("os:reboot-required","os","unknown"); }
    }
    private static DiagnosticCheckMetrics UpdateStatus()
    {
        try { if(OperatingSystem.IsLinux()){var start=new ProcessStartInfo("apt-get"){RedirectStandardOutput=true,RedirectStandardError=true,UseShellExecute=false,CreateNoWindow=true};start.ArgumentList.Add("-s");start.ArgumentList.Add("upgrade");using var p=Process.Start(start)!;var output=p.StandardOutput.ReadToEnd();p.WaitForExit(15000);if(!p.HasExited){p.Kill(true);return new("os:update-status","os-update","unknown","check timeout");}var count=output.Split('\n').Count(x=>x.StartsWith("Inst ",StringComparison.Ordinal));return new("os:update-status","os-update",count==0?"current":"updates-available",$"{count} package(s)");}return new("os:update-status","os-update","managed-by-windows-update"); } catch { return new("os:update-status","os-update","unknown"); }
    }
}
// CoreWatch Atlas module: DiagnosticChecks.
