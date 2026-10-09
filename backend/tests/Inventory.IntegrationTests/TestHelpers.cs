using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Inventory.IntegrationTests;

internal static class Http
{
    public static CancellationToken Ct => TestContext.Current.CancellationToken;

    public static async Task<T> ReadAsync<T>(this HttpResponseMessage response)
    {
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(Ct);
            throw new InvalidOperationException($"Expected success but got {(int)response.StatusCode}: {body}");
        }
        return (await response.Content.ReadFromJsonAsync<T>(Json.Options, Ct))!;
    }

    public static async Task<JsonElement> ProblemAsync(this HttpResponseMessage response, HttpStatusCode expected)
    {
        var body = await response.Content.ReadAsStringAsync(Ct);
        response.StatusCode.ShouldBe(expected, body);
        return JsonDocument.Parse(body).RootElement.Clone();
    }

    public static string Code(this JsonElement problem) => problem.TryGetProperty("code", out var c) ? c.GetString()! : "";

    public static Task<HttpResponseMessage> PostJsonAsync(this HttpClient client, string url, object body) => client.PostAsJsonAsync(url, body, Json.Options, Ct);

    public static Task<HttpResponseMessage> PutJsonAsync(this HttpClient client, string url, object body) => client.PutAsJsonAsync(url, body, Json.Options, Ct);

    public static Task<HttpResponseMessage> PostAsync(this HttpClient client, string url) => client.PostAsync(url, null, Ct);

    public static Task<T?> GetJsonAsync<T>(this HttpClient client, string url) => client.GetFromJsonAsync<T>(url, Json.Options, Ct);

    public static string Unique(string prefix) => $"{prefix}-{Guid.NewGuid().ToString("N")[..8]}".ToUpperInvariant();
}
