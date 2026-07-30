using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Runtime.InteropServices;
using CoreWatch.Atlas.Agent;
using CoreWatch.Atlas.Collectors.Linux;
using CoreWatch.Atlas.Collectors.Windows;

var registerBaseUrl = ReadOption(args, "--register-agent");
var updateHandoff = ReadOption(args, "--apply-agent-update");
if (updateHandoff is not null)
{
    Environment.ExitCode = await AgentUpdateInstaller.RunAsync(updateHandoff);
    return;
}
var rotateCredential = args.Contains("--rotate-agent-credential", StringComparer.Ordinal);
if (registerBaseUrl is not null && rotateCredential)
{
    throw new InvalidOperationException("Only one Agent credential command can run at a time.");
}

var builder = Host.CreateApplicationBuilder(RemoveCredentialCommandArguments(args));
var credentialStore = new AgentCredentialStore(builder.Configuration, builder.Environment);
if (registerBaseUrl is not null)
{
    await RegisterAgentAsync(registerBaseUrl, credentialStore);
    return;
}

if (rotateCredential)
{
    await RotateCredentialAsync(credentialStore);
    return;
}

ApplyStoredCredentials(builder.Configuration, credentialStore.Load());
if (OperatingSystem.IsWindows())
{
    builder.Services.AddAtlasMetricsCollection<WindowsSystemMetricsCollector>(builder.Configuration);
}
else if (OperatingSystem.IsLinux())
{
    builder.Services.AddAtlasMetricsCollection<LinuxSystemMetricsCollector>(builder.Configuration);
}
else
{
    builder.Services.AddAtlasMetricsCollection<UnconfiguredSystemMetricsCollector>(builder.Configuration);
}

var host = builder.Build();
host.Run();

static async Task RegisterAgentAsync(string baseUrl, AgentCredentialStore credentialStore)
{
    if (!AgentCredentialStore.TryValidateServerUri(baseUrl, out var serverUri))
    {
        throw new InvalidOperationException("Agent registration requires HTTPS, except for a loopback HTTP server.");
    }

    var token = Environment.GetEnvironmentVariable(
        "COREWATCH_ATLAS_REGISTRATION_TOKEN");
    if (string.IsNullOrWhiteSpace(token))
    {
        token = ReadSecret("One-time registration token: ");
    }
    Environment.SetEnvironmentVariable(
        "COREWATCH_ATLAS_REGISTRATION_TOKEN",
        null);
    var existing = credentialStore.Load();
    using var client = CreateAdministrationClient(serverUri!);
    using var response = await client.PostAsJsonAsync(
        "api/v1/agents/register",
        new AgentRegistrationRequest(
            token,
            Environment.MachineName,
            RuntimeInformation.OSDescription,
            RuntimeInformation.OSArchitecture.ToString(),
            typeof(Program).Assembly.GetName().Version?.ToString() ?? "unknown",
            existing?.AgentId));
    response.EnsureSuccessStatusCode();
    var registered = await response.Content.ReadFromJsonAsync<RegisteredAgent>()
        ?? throw new InvalidOperationException("The Atlas Server returned an empty registration response.");
    credentialStore.Save(new StoredAgentCredentials(serverUri!.ToString(), registered.AgentId, registered.Credential));
    Console.WriteLine($"Registered Agent '{registered.AgentId:D}' and stored its credential.");
}

static async Task RotateCredentialAsync(AgentCredentialStore credentialStore)
{
    var stored = credentialStore.Load()
        ?? throw new InvalidOperationException("No stored Atlas Agent credentials were found.");
    using var client = CreateAdministrationClient(new Uri(stored.BaseUrl));
    client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", stored.Credential);
    using var response = await client.PostAsync($"api/v1/agents/{stored.AgentId:D}/credentials/rotate", content: null);
    response.EnsureSuccessStatusCode();
    var rotated = await response.Content.ReadFromJsonAsync<AgentCredentialResponse>()
        ?? throw new InvalidOperationException("The Atlas Server returned an empty credential rotation response.");
    credentialStore.Save(stored with { Credential = rotated.Credential });
    Console.WriteLine($"Rotated and stored the credential for Agent '{stored.AgentId:D}'.");
}

static HttpClient CreateAdministrationClient(Uri baseUri) =>
    new() { BaseAddress = baseUri, Timeout = TimeSpan.FromSeconds(15) };

static void ApplyStoredCredentials(IConfiguration configuration, StoredAgentCredentials? stored)
{
    if (stored is null
        || !string.IsNullOrWhiteSpace(configuration[$"{ServerTransmissionOptions.SectionName}:Credential"]))
    {
        return;
    }

    var baseUrlKey = $"{ServerTransmissionOptions.SectionName}:BaseUrl";
    var configuredBaseUrl = configuration[baseUrlKey];
    configuration[$"{ServerTransmissionOptions.SectionName}:Enabled"] = "true";
    configuration[baseUrlKey] = string.IsNullOrWhiteSpace(configuredBaseUrl)
        ? stored.BaseUrl
        : configuredBaseUrl;
    configuration[$"{ServerTransmissionOptions.SectionName}:AgentId"] = stored.AgentId.ToString("D");
    configuration[$"{ServerTransmissionOptions.SectionName}:Credential"] = stored.Credential;
}

static string? ReadOption(string[] arguments, string option)
{
    for (var index = 0; index < arguments.Length; index++)
    {
        if (!string.Equals(arguments[index], option, StringComparison.Ordinal))
        {
            continue;
        }

        if (index + 1 >= arguments.Length || arguments[index + 1].StartsWith("--", StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"{option} requires a value.");
        }

        return arguments[index + 1];
    }

    return null;
}

static string[] RemoveCredentialCommandArguments(string[] arguments)
{
    var result = new List<string>();
    for (var index = 0; index < arguments.Length; index++)
    {
        if (string.Equals(arguments[index], "--rotate-agent-credential", StringComparison.Ordinal))
        {
            continue;
        }

        if (string.Equals(arguments[index], "--register-agent", StringComparison.Ordinal))
        {
            index++;
            continue;
        }
        if (string.Equals(arguments[index], "--apply-agent-update", StringComparison.Ordinal))
        {
            index++;
            continue;
        }

        result.Add(arguments[index]);
    }

    return result.ToArray();
}

static string ReadSecret(string prompt)
{
    if (Console.IsInputRedirected)
    {
        throw new InvalidOperationException("Agent credential commands require an interactive terminal.");
    }

    Console.Write(prompt);
    var value = new List<char>();
    while (true)
    {
        var key = Console.ReadKey(intercept: true);
        if (key.Key == ConsoleKey.Enter)
        {
            Console.WriteLine();
            return new string(value.ToArray());
        }

        if (key.Key == ConsoleKey.Backspace && value.Count > 0)
        {
            value.RemoveAt(value.Count - 1);
        }
        else if (!char.IsControl(key.KeyChar))
        {
            value.Add(key.KeyChar);
        }
    }
}

internal sealed record AgentRegistrationRequest(
    string RegistrationToken,
    string HostName,
    string OperatingSystem,
    string Architecture,
    string AgentVersion,
    Guid? ExistingAgentId = null);

internal sealed record RegisteredAgent(Guid AgentId, DateTimeOffset RegisteredAtUtc, string Credential);

internal sealed record AgentCredentialResponse(Guid AgentId, string Credential, DateTimeOffset IssuedAtUtc);

public partial class Program;
