using System.Net;
using System.Text.Json;
using CloudSharp.Api.IntegrationTests.TestHost;
using Microsoft.AspNetCore.Hosting;
using NUnit.Framework;

namespace CloudSharp.Api.IntegrationTests.Contracts;

[TestFixture]
[Category("ApiIntegration")]
[NonParallelizable]
public sealed class IdempotencyOpenApiEndpointTests : ApiIntegrationTestBase
{
    [Test]
    public async Task GetDocument_ShouldRequireTheKeyOnOptedInEndpoints()
    {
        await using var factory = Environment.Factory.WithWebHostBuilder(builder => builder.UseEnvironment("Development"));
        using var client = new TestApiClient(factory);
        using var response = await client.GetAsync("/openapi/v1.json");
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        using var document = JsonDocument.Parse(await TestApiClient.ReadBodyAsync(response));

        var keyedPaths = new List<string>();
        foreach (var path in document.RootElement.GetProperty("paths").EnumerateObject())
        {
            if (!path.Value.TryGetProperty("post", out var operation) ||
                !operation.TryGetProperty("parameters", out var parameters))
            {
                continue;
            }

            foreach (var parameter in parameters.EnumerateArray().Where(parameter =>
                         parameter.TryGetProperty("name", out var name) &&
                         name.GetString() == TestApiClient.IdempotencyKeyHeaderName))
            {
                Assert.That(parameter.GetProperty("in").GetString(), Is.EqualTo("header"));
                Assert.That(parameter.GetProperty("required").GetBoolean(), Is.True);
                keyedPaths.Add(path.Name.TrimEnd('/'));
            }
        }

        Assert.That(keyedPaths, Is.EquivalentTo(new[]
        {
            "/api/v2/auth/registrations",
            "/api/v2/spaces",
            "/api/v2/spaces/{spacePublicId}/invites",
            "/api/v2/invites/{inviteToken}/accept",
        }));
    }
}
