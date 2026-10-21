using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace CloudSharp.Api.IntegrationTests.TestHost;

internal sealed class TestApiClient : IDisposable
{
    internal const string CorrelationHeaderName = "X-Correlation-ID";
    internal const string IfMatchHeaderName = "If-Match";

    internal static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _http;

    internal TestApiClient(WebApplicationFactory<Program> factory)
    {
        _http = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            AllowAutoRedirect = false,
            HandleCookies = false,
        });
        _http.Timeout = TimeSpan.FromSeconds(90);
    }

    internal HttpClient Http => _http;

    internal Task<HttpResponseMessage> GetAsync(
        string path,
        string? token = null,
        string? correlationId = null,
        Action<HttpRequestMessage>? configure = null) =>
        SendAsync(HttpMethod.Get, path, token, correlationId, configure);

    internal Task<HttpResponseMessage> SendJsonAsync<T>(
        HttpMethod method,
        string path,
        T body,
        string? token = null,
        string? ifMatch = null,
        string? correlationId = null,
        Action<HttpRequestMessage>? configure = null) =>
        SendAsync(method, path, token, correlationId, request =>
        {
            request.Content = new StringContent(
                JsonSerializer.Serialize(body, JsonOptions),
                Encoding.UTF8,
                "application/json");
            if (ifMatch is not null)
            {
                request.Headers.TryAddWithoutValidation(IfMatchHeaderName, ifMatch);
            }

            configure?.Invoke(request);
        });

    internal Task<HttpResponseMessage> SendRawAsync(
        HttpMethod method,
        string path,
        string content,
        string mediaType,
        string? token = null,
        string? ifMatch = null,
        string? correlationId = null,
        Action<HttpRequestMessage>? configure = null) =>
        SendAsync(method, path, token, correlationId, request =>
        {
            request.Content = new StringContent(content, Encoding.UTF8, mediaType);
            if (ifMatch is not null)
            {
                request.Headers.TryAddWithoutValidation(IfMatchHeaderName, ifMatch);
            }

            configure?.Invoke(request);
        });

    internal async Task<HttpResponseMessage> SendAsync(
        HttpMethod method,
        string path,
        string? token = null,
        string? correlationId = null,
        Action<HttpRequestMessage>? configure = null)
    {
        using var request = new HttpRequestMessage(method, path);
        if (token is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        if (correlationId is not null)
        {
            request.Headers.TryAddWithoutValidation(CorrelationHeaderName, correlationId);
        }

        configure?.Invoke(request);
        return await _http.SendAsync(request);
    }

    internal static async Task<T> ReadJsonAsync<T>(HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync();
        var value = JsonSerializer.Deserialize<T>(body, JsonOptions);
        return value ?? throw new InvalidOperationException(
            "The response body did not contain the expected JSON document.");
    }

    internal static async Task<ErrorEnvelope> ReadErrorAsync(HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync();
        return JsonSerializer.Deserialize<ErrorEnvelope>(body, JsonOptions)
            ?? throw new InvalidOperationException(
                "The error response body did not contain the expected JSON document.");
    }

    internal static Task<string> ReadBodyAsync(HttpResponseMessage response) =>
        response.Content.ReadAsStringAsync();

    internal static string? GetHeader(HttpResponseMessage response, string name) =>
        response.Headers.TryGetValues(name, out var values)
            ? string.Join(",", values)
            : null;

    public void Dispose() => _http.Dispose();
}
