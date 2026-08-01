using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace CoreWatch.Atlas.Server;

internal sealed class GitHubReleaseCatalog(
    IHttpClientFactory clients,
    IOptionsMonitor<GitHubReleaseOptions> options,
    TimeProvider timeProvider,
    ILogger<GitHubReleaseCatalog> logger)
{
    private readonly SemaphoreSlim gate = new(1, 1);
    private PublishedRelease? cached;
    private DateTimeOffset expiresAtUtc = DateTimeOffset.MinValue;

    public async Task<PublishedRelease?> GetLatestAsync(CancellationToken cancellationToken)
    {
        var settings = options.CurrentValue;
        if (!settings.Enabled || !IsRepository(settings.Repository)) return null;
        // GitHub API 호출은 설치된 Agent 수와 무관하게 Server 한 곳에서만 수행한다.
        // 캐시로 API 제한과 일시적인 외부망 오류의 영향을 줄인다.
        if (cached is not null && timeProvider.GetUtcNow() < expiresAtUtc) return cached;
        await gate.WaitAsync(cancellationToken);
        try
        {
            if (cached is not null && timeProvider.GetUtcNow() < expiresAtUtc) return cached;
            using var request = new HttpRequestMessage(HttpMethod.Get, $"repos/{settings.Repository}/releases/latest");
            request.Headers.UserAgent.Add(new ProductInfoHeaderValue("CoreWatch-Atlas", "1.0"));
            using var response = await clients.CreateClient("atlas-github-release").SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("GitHub release lookup returned HTTP {StatusCode}.", (int)response.StatusCode);
                return null;
            }
            using var document = JsonDocument.Parse(await response.Content.ReadAsStreamAsync(cancellationToken));
            var candidate = TryParse(document.RootElement);
            cached = candidate is null ? null : await ResolveHashesAsync(candidate, cancellationToken);
            expiresAtUtc = timeProvider.GetUtcNow().AddMinutes(Math.Clamp(settings.CacheMinutes, 5, 1440));
            return cached;
        }
        catch (HttpRequestException exception)
        {
            logger.LogWarning(exception, "GitHub release lookup failed.");
            return null;
        }
        finally { gate.Release(); }
    }

    internal static GitHubReleaseCandidate? TryParse(JsonElement release)
    {
        if (!release.TryGetProperty("tag_name", out var tag)) return null;
        var version = tag.GetString()?.TrimStart('v', 'V');
        if (!Version.TryParse(version, out _)) return null;
        var assets = release.TryGetProperty("assets", out var value) && value.ValueKind == JsonValueKind.Array ? value.EnumerateArray().ToArray() : [];
        return Create(version!, assets, "corewatch-atlas-server.zip", "corewatch-atlas-agent.zip");
    }

    private async Task<PublishedRelease?> ResolveHashesAsync(GitHubReleaseCandidate candidate, CancellationToken token)
    {
        var serverHash = await DownloadHashAsync(candidate.ServerHashUrl, token);
        var agentHash = await DownloadHashAsync(candidate.AgentHashUrl, token);
        return serverHash is null || agentHash is null ? null : new PublishedRelease(
            candidate.Version, candidate.ServerPackageUrl, serverHash,
            candidate.AgentPackageUrl, agentHash);
    }

    private async Task<string?> DownloadHashAsync(string url, CancellationToken token)
    {
        using var response = await clients.CreateClient("atlas-github-release").GetAsync(url, token);
        if (!response.IsSuccessStatusCode) return null;
        // Publish 스크립트가 만드는 해시 파일은 "SHA256 경로" 형식이다.
        // 첫 토큰만 받아 64자리 16진수인지 검증한 뒤에만 업데이트 매니페스트에 사용한다.
        var hash = (await response.Content.ReadAsStringAsync(token)).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        return hash is { Length: 64 } && hash.All(Uri.IsHexDigit) ? hash.ToUpperInvariant() : null;
    }

    private static GitHubReleaseCandidate? Create(string version, JsonElement[] assets, string serverName, string agentName)
    {
        string? Url(string name) => assets.FirstOrDefault(x => x.TryGetProperty("name", out var n) && n.GetString() == name).TryGetProperty("browser_download_url", out var u) ? u.GetString() : null;
        var server = Url(serverName); var agent = Url(agentName); var serverHash = Url(serverName + ".sha256.txt"); var agentHash = Url(agentName + ".sha256.txt");
        return server is null || agent is null || serverHash is null || agentHash is null
            ? null : new GitHubReleaseCandidate(version, server, serverHash, agent, agentHash);
    }

    private static bool IsRepository(string value) => value.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Length == 2;
}

internal sealed record GitHubReleaseCandidate(string Version, string ServerPackageUrl, string ServerHashUrl, string AgentPackageUrl, string AgentHashUrl);
