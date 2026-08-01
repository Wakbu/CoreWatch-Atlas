using System.Net.Http.Json;
using System.Text.Json;

namespace CoreWatch.Atlas.Server.Tests;

internal static class SecurityTestClient
{
    public static async Task<HttpResponseMessage> PostAsJsonWithCsrfAsync<T>(
        HttpClient client,
        string path,
        T value)
    {
        using var request = await CreatePostRequestAsync(client, path);
        request.Content = JsonContent.Create(value);
        return await client.SendAsync(request);
    }

    public static async Task<HttpResponseMessage> PostWithCsrfAsync(
        HttpClient client,
        string path)
    {
        using var request = await CreatePostRequestAsync(client, path);
        return await client.SendAsync(request);
    }

    public static async Task<HttpResponseMessage> SendWithCsrfAsync(
        HttpClient client,
        HttpMethod method,
        string path,
        HttpContent? content = null)
    {
        var response = await client.GetFromJsonAsync<JsonElement>(
            "/api/v1/auth/csrf");
        using var request = new HttpRequestMessage(method, path)
        {
            Content = content,
        };
        request.Headers.Add(
            ServerSecurity.AntiforgeryHeaderName,
            response.GetProperty("token").GetString());
        return await client.SendAsync(request);
    }
    private static async Task<HttpRequestMessage> CreatePostRequestAsync(
        HttpClient client,
        string path)
    {
        var response = await client.GetFromJsonAsync<JsonElement>(
            "/api/v1/auth/csrf");
        var request = new HttpRequestMessage(HttpMethod.Post, path);
        request.Headers.Add(
            ServerSecurity.AntiforgeryHeaderName,
            response.GetProperty("token").GetString());
        return request;
    }
}
// CoreWatch Atlas module: SecurityTestClient.
