using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using CoreWatch.Atlas.Contracts;
using Microsoft.Extensions.Options;

namespace CoreWatch.Atlas.Agent;

public sealed class AtlasServerClient
{
    private readonly HttpClient httpClient;
    private readonly bool enabled;
    private readonly Uri? baseUri;
    private readonly Guid agentId;
    private readonly string? credential;

    public AtlasServerClient(
        HttpClient httpClient,
        IOptions<ServerTransmissionOptions> options)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(options);

        this.httpClient = httpClient;
        var settings = options.Value;
        enabled = settings.Enabled;
        if (!enabled)
        {
            return;
        }

        if (!AgentCredentialStore.TryValidateServerUri(
                settings.BaseUrl,
                out baseUri))
        {
            throw new InvalidOperationException(
                "Enabled Atlas server transmission requires HTTPS, except for loopback HTTP.");
        }

        if (!Guid.TryParse(settings.AgentId, out agentId))
        {
            throw new InvalidOperationException(
                "Enabled Atlas server transmission requires a valid AgentId.");
        }

        if (string.IsNullOrWhiteSpace(settings.Credential)
            || settings.Credential.Length > 128)
        {
            throw new InvalidOperationException(
                "Enabled Atlas server transmission requires an Agent credential.");
        }

        credential = settings.Credential;
    }

    public async Task SendAsync(
        SystemMetricsSnapshot snapshot,
        CancellationToken cancellationToken)
    {
        if (!enabled)
        {
            return;
        }

        var serverSnapshot = new SystemMetricsSnapshot(
            snapshot.CapturedAtUtc,
            snapshot.Uptime,
            new AgentIdentity(
                agentId.ToString("D"),
                snapshot.Agent.HostName,
                snapshot.Agent.OperatingSystem,
                snapshot.Agent.Architecture,
                snapshot.Agent.AgentVersion),
            snapshot.Cpu,
            snapshot.Memory,
            snapshot.FileSystems,
            snapshot.Disks,
            snapshot.NetworkInterfaces,
            snapshot.Services,
            snapshot.Diagnostics);
        var requestUri = new Uri(
            baseUri!,
            $"api/v1/agents/{agentId:D}/snapshots");
        using var request = new HttpRequestMessage(HttpMethod.Post, requestUri)
        {
            Content = JsonContent.Create(serverSnapshot),
        };
        request.Headers.Authorization =
            new AuthenticationHeaderValue("Bearer", credential);
        using var response = await httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    internal async Task<AgentUpdateManifest?> GetPendingUpdateAsync(
        CancellationToken cancellationToken)
    {
        if (!enabled)
        {
            return null;
        }
        using var request = CreateAgentRequest(
            HttpMethod.Get, $"api/v1/agents/{agentId:D}/updates/pending");
        using var response = await httpClient.SendAsync(request, cancellationToken);
        if (response.StatusCode == System.Net.HttpStatusCode.NoContent)
        {
            return null;
        }
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<AgentUpdateManifest>(
            cancellationToken: cancellationToken)
            ?? throw new InvalidDataException("The Atlas Server returned an empty update manifest.");
    }

    internal async Task ReportUpdateStatusAsync(
        long deploymentId,
        string state,
        string? detail,
        CancellationToken cancellationToken)
    {
        if (!enabled)
        {
            return;
        }
        using var request = CreateAgentRequest(
            HttpMethod.Post,
            $"api/v1/agents/{agentId:D}/updates/{deploymentId}/status");
        request.Content = JsonContent.Create(new AgentUpdateStatusRequest(state, detail));
        using var response = await httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    internal async Task<AgentCommand?> GetPendingCommandAsync(CancellationToken cancellationToken)
    {
        if(!enabled)return null;using var request=CreateAgentRequest(HttpMethod.Get,$"api/v1/agents/{agentId:D}/commands/pending");using var response=await httpClient.SendAsync(request,cancellationToken);if(response.StatusCode==System.Net.HttpStatusCode.NoContent)return null;response.EnsureSuccessStatusCode();return await response.Content.ReadFromJsonAsync<AgentCommand>(cancellationToken:cancellationToken);
    }
    internal async Task ReportCommandAsync(long id,string state,string? detail,CancellationToken cancellationToken)
    {
        if(!enabled)return;using var request=CreateAgentRequest(HttpMethod.Post,$"api/v1/agents/{agentId:D}/commands/{id}/status");request.Content=JsonContent.Create(new AgentCommandStatus(state,detail));using var response=await httpClient.SendAsync(request,cancellationToken);response.EnsureSuccessStatusCode();
    }
    internal async Task<DiagnosticsOptions?> GetDiagnosticsConfigurationAsync(CancellationToken cancellationToken)
    { if(!enabled)return null;using var request=CreateAgentRequest(HttpMethod.Get,$"api/v1/agents/{agentId:D}/diagnostics/config");using var response=await httpClient.SendAsync(request,cancellationToken);response.EnsureSuccessStatusCode();return await response.Content.ReadFromJsonAsync<DiagnosticsOptions>(cancellationToken:cancellationToken); }

    internal async Task DownloadUpdateAsync(
        AgentUpdateManifest manifest,
        string destination,
        CancellationToken cancellationToken)
    {
        if (!enabled || baseUri is null)
        {
            throw new InvalidOperationException("Atlas server transmission is disabled.");
        }
        if (!TryValidateManifest(manifest, baseUri, out var packageUri))
        {
            throw new InvalidDataException("The Agent update manifest is invalid or untrusted.");
        }
        using var request = new HttpRequestMessage(HttpMethod.Get, packageUri);
        if (baseUri.IsBaseOf(packageUri!))
        {
            request.Headers.Authorization =
                new AuthenticationHeaderValue("Bearer", credential);
        }
        using var response = await httpClient.SendAsync(
            request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        await using var source = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using var target = new FileStream(
            destination, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true);
        using var sha256 = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[81920];
        int count;
        while ((count = await source.ReadAsync(buffer, cancellationToken)) > 0)
        {
            await target.WriteAsync(buffer.AsMemory(0, count), cancellationToken);
            sha256.AppendData(buffer, 0, count);
        }
        var actual = Convert.ToHexString(sha256.GetHashAndReset());
        if (!actual.Equals(manifest.Sha256, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("The Agent update package SHA-256 does not match the manifest.");
        }
    }

    internal static bool TryValidateManifest(
        AgentUpdateManifest manifest,
        Uri serverBaseUri,
        out Uri? packageUri)
    {
        packageUri = null;
        return manifest.DeploymentId > 0
            && Version.TryParse(manifest.Version, out _)
            && manifest.Sha256.Length == 64
            && manifest.Sha256.All(Uri.IsHexDigit)
            && Uri.TryCreate(manifest.PackageUrl, UriKind.Absolute, out packageUri)
            && (packageUri.Scheme == Uri.UriSchemeHttps
                || (packageUri.IsLoopback && serverBaseUri.IsLoopback));
    }

    private HttpRequestMessage CreateAgentRequest(HttpMethod method, string relativeUri)
    {
        var request = new HttpRequestMessage(method, new Uri(baseUri!, relativeUri));
        request.Headers.Authorization =
            new AuthenticationHeaderValue("Bearer", credential);
        return request;
    }
}
