using System.Net;
using System.Text.Json;
using NUnit.Framework;

namespace CloudSharp.Api.IntegrationTests.TestHost;

internal static class TestFlows
{
    internal static async Task<AuthSessionWireResponse> RegisterAsync(
        TestApiClient api,
        RegistrationWireRequest request)
    {
        using var response = await api.SendJsonAsync(
            HttpMethod.Post,
            "/api/v2/auth/registrations",
            request);
        return await ReadSuccessAsync<AuthSessionWireResponse>(
            response,
            HttpStatusCode.Created,
            "Registration");
    }

    internal static async Task<AuthSessionWireResponse> LoginAsync(
        TestApiClient api,
        LoginWireRequest request)
    {
        using var response = await api.SendJsonAsync(
            HttpMethod.Post,
            "/api/v2/auth/sessions",
            request);
        return await ReadSuccessAsync<AuthSessionWireResponse>(
            response,
            HttpStatusCode.OK,
            "Login");
    }

    internal static async Task<(UserWireResponse User, string ETag)> GetMeAsync(
        TestApiClient api,
        string token)
    {
        using var response = await api.GetAsync("/api/v2/me", token);
        var user = await ReadSuccessAsync<UserWireResponse>(
            response,
            HttpStatusCode.OK,
            "Get profile");
        var etag = TestApiClient.GetHeader(response, "ETag");
        Assert.That(etag, Is.Not.Null.And.Not.Empty, "GET /api/v2/me must return an ETag.");
        return (user, etag!);
    }

    private static async Task<T> ReadSuccessAsync<T>(
        HttpResponseMessage response,
        HttpStatusCode expectedStatus,
        string action)
    {
        if (response.StatusCode != expectedStatus)
        {
            var error = await TryReadErrorAsync(response);
            throw new AssertionException(
                $"{action} expected {(int)expectedStatus} but returned "
                + $"{(int)response.StatusCode} ({error?.Error.Code ?? "NO_ERROR_BODY"}).");
        }

        return await TestApiClient.ReadJsonAsync<T>(response);
    }

    private static async Task<ErrorEnvelope?> TryReadErrorAsync(HttpResponseMessage response)
    {
        try
        {
            return await TestApiClient.ReadErrorAsync(response);
        }
        catch (JsonException)
        {
            return null;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }
}
