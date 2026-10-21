using System.Net;
using System.Text.Json;
using NUnit.Framework;

namespace CloudSharp.Api.IntegrationTests.TestHost;

internal static class IntegrationAssert
{
    internal static async Task<ErrorEnvelope> AssertErrorAsync(
        HttpResponseMessage response,
        HttpStatusCode expectedStatus,
        string expectedCode)
    {
        Assert.That(response.StatusCode, Is.EqualTo(expectedStatus));
        AssertJsonContentType(response);
        AssertNoStore(response);

        var body = await TestApiClient.ReadBodyAsync(response);
        var error = JsonSerializer.Deserialize<ErrorEnvelope>(body, TestApiClient.JsonOptions);
        Assert.That(error, Is.Not.Null, "The error response must use the standard JSON envelope.");

        var correlationId = TestApiClient.GetHeader(
            response,
            TestApiClient.CorrelationHeaderName);

        Assert.Multiple(() =>
        {
            Assert.That(error!.Error.Code, Is.EqualTo(expectedCode));
            Assert.That(error.Error.Details, Is.Not.Null);
            Assert.That(correlationId, Is.Not.Null.And.Not.Empty);
            Assert.That(error.RequestId, Is.EqualTo(correlationId));
        });

        return error;
    }

    internal static void AssertNoStore(HttpResponseMessage response)
    {
        var cacheControl = TestApiClient.GetHeader(response, "Cache-Control");
        Assert.That(cacheControl, Is.EqualTo("no-store"));
    }

    internal static void AssertJsonContentType(HttpResponseMessage response)
    {
        Assert.That(response.Content.Headers.ContentType?.MediaType, Is.EqualTo("application/json"));
    }

    internal static void AssertUtc(DateTimeOffset value, string fieldName)
    {
        Assert.That(value.Offset, Is.EqualTo(TimeSpan.Zero), $"{fieldName} must be UTC.");
    }

    internal static void AssertUuidV7(Guid value, string fieldName)
    {
        var text = value.ToString("D");
        Assert.Multiple(() =>
        {
            Assert.That(value, Is.Not.EqualTo(Guid.Empty), $"{fieldName} must not be empty.");
            Assert.That(text[14], Is.EqualTo('7'), $"{fieldName} must be a UUIDv7 value.");
        });
    }

    internal static void AssertNoInternalIdentifiers(string json)
    {
        Assert.Multiple(() =>
        {
            Assert.That(json.Contains("\"passwordHash\"", StringComparison.Ordinal), Is.False);
            Assert.That(json.Contains("\"tokenHash\"", StringComparison.Ordinal), Is.False);
            Assert.That(json.Contains("\"securityVersion\"", StringComparison.Ordinal), Is.False);
            Assert.That(json.Contains("\"version\"", StringComparison.Ordinal), Is.False);
        });
    }

    internal static void AssertDoesNotContain(string body, string secret, string description)
    {
        Assert.That(
            body.Contains(secret, StringComparison.Ordinal),
            Is.False,
            description);
    }

    internal static void AssertOnlyPublicUserProperties(JsonElement user) =>
        AssertPropertyNames(
            user,
            "userPublicId",
            "email",
            "userName",
            "displayName",
            "systemRole",
            "status",
            "createdAt",
            "updatedAt");

    internal static void AssertPropertyNames(JsonElement element, params string[] expected)
    {
        var actual = element.EnumerateObject()
            .Select(property => property.Name)
            .ToHashSet(StringComparer.Ordinal);

        Assert.That(
            actual.SetEquals(expected),
            Is.True,
            "The JSON property names do not match the public contract.");
    }
}
