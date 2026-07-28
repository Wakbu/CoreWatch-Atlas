using System.Net.Http.Headers;
using System.Net.Http.Json;
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

        if (!Uri.TryCreate(settings.BaseUrl, UriKind.Absolute, out baseUri)
            || baseUri.Scheme is not ("http" or "https"))
        {
            throw new InvalidOperationException(
                "Enabled Atlas server transmission requires an absolute HTTP(S) BaseUrl.");
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
            snapshot.NetworkInterfaces);
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
}
