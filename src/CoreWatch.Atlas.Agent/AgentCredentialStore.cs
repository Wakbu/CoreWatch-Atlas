using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;

namespace CoreWatch.Atlas.Agent;

internal sealed record StoredAgentCredentials(
    string BaseUrl,
    Guid AgentId,
    string Credential);

internal sealed class AgentCredentialStore
{
    private const string Purpose = "CoreWatch-Atlas.Agent.Credentials.v1";
    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web);
    private readonly string credentialPath;
    private readonly IDataProtector protector;

    public AgentCredentialStore(
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        var configuredPath = configuration[
            $"{AgentCredentialStoreOptions.SectionName}:Path"];
        if (string.IsNullOrWhiteSpace(configuredPath))
        {
            configuredPath = new AgentCredentialStoreOptions().Path;
        }

        var directoryPath = System.IO.Path.GetFullPath(
            System.IO.Path.IsPathRooted(configuredPath)
                ? configuredPath
                : System.IO.Path.Combine(
                    environment.ContentRootPath,
                    configuredPath));
        Directory.CreateDirectory(directoryPath);
        RestrictDirectoryToOwner(directoryPath);
        credentialPath = System.IO.Path.Combine(
            directoryPath,
            "credentials.protected");

        var keyDirectory = Directory.CreateDirectory(
            System.IO.Path.Combine(directoryPath, "keys"));
        RestrictDirectoryToOwner(keyDirectory.FullName);
        var provider = DataProtectionProvider.Create(
            keyDirectory,
            configurationBuilder =>
            {
                configurationBuilder.SetApplicationName(
                    "CoreWatch-Atlas.Agent");
                if (OperatingSystem.IsWindows())
                {
                    configurationBuilder.ProtectKeysWithDpapi(protectToLocalMachine: true);
                }
            });
        protector = provider.CreateProtector(Purpose);
    }

    public StoredAgentCredentials? Load()
    {
        if (!File.Exists(credentialPath))
        {
            return null;
        }

        try
        {
            var protectedPayload = File.ReadAllBytes(credentialPath);
            var payload = protector.Unprotect(protectedPayload);
            return Validate(
                JsonSerializer.Deserialize<StoredAgentCredentials>(
                    payload,
                    JsonOptions));
        }
        catch (Exception exception)
            when (exception is CryptographicException
                or JsonException
                or IOException
                or UnauthorizedAccessException)
        {
            throw new InvalidOperationException(
                "Stored Atlas Agent credentials could not be read or decrypted.",
                exception);
        }
    }

    public void Save(StoredAgentCredentials credentials)
    {
        Validate(credentials);
        var payload = JsonSerializer.SerializeToUtf8Bytes(
            credentials,
            JsonOptions);
        var protectedPayload = protector.Protect(payload);
        var temporaryPath = credentialPath + ".tmp";
        try
        {
            File.WriteAllBytes(temporaryPath, protectedPayload);
            RestrictFileToOwner(temporaryPath);
            File.Move(temporaryPath, credentialPath, overwrite: true);
            RestrictFileToOwner(credentialPath);
        }
        finally
        {
            File.Delete(temporaryPath);
        }
    }

    private static StoredAgentCredentials Validate(
        StoredAgentCredentials? credentials)
    {
        if (credentials is null
            || credentials.AgentId == Guid.Empty
            || string.IsNullOrWhiteSpace(credentials.Credential)
            || credentials.Credential.Length > 128
            || !TryValidateServerUri(credentials.BaseUrl, out _))
        {
            throw new InvalidOperationException(
                "Stored Atlas Agent credentials are invalid.");
        }

        return credentials;
    }

    internal static bool TryValidateServerUri(
        string value,
        out Uri? baseUri)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out baseUri))
        {
            return false;
        }

        return baseUri.Scheme == Uri.UriSchemeHttps
            || (baseUri.Scheme == Uri.UriSchemeHttp && baseUri.IsLoopback);
    }

    private static void RestrictDirectoryToOwner(string path)
    {
        if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
        {
            File.SetUnixFileMode(
                path,
                UnixFileMode.UserRead
                | UnixFileMode.UserWrite
                | UnixFileMode.UserExecute);
        }
    }

    private static void RestrictFileToOwner(string path)
    {
        if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
        {
            File.SetUnixFileMode(
                path,
                UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
    }
}
// CoreWatch Atlas module: AgentCredentialStore.
