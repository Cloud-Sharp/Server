using System.Text.Json;
using CloudSharp.Api.DependencyInjection;
using CloudSharp.Api.Endpoints.Spaces.Requests;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NUnit.Framework;

namespace CloudSharp.Api.Tests.Endpoints.Spaces;

[TestFixture]
public class SpaceRequestContractTests
{
    private static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions();

    [TestCase("""{}""", false, null)]
    [TestCase("""{"description":null}""", true, null)]
    [TestCase("""{"description":"New description"}""", true, "New description")]
    public void UpdateSpaceRequest_ShouldPreserveWhetherDescriptionWasSupplied(
        string json,
        bool hasDescription,
        string? description)
    {
        var request = JsonSerializer.Deserialize<UpdateSpaceRequest>(json, JsonOptions)!;

        Assert.Multiple(() =>
        {
            Assert.That(request.HasDescription, Is.EqualTo(hasDescription));
            Assert.That(request.Description, Is.EqualTo(description));
        });
    }

    [TestCase("""{"name":null}""")]
    [TestCase("""{"hasDescription":true}""")]
    public void UpdateSpaceRequest_ShouldRejectInvalidFields(string json)
    {
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<UpdateSpaceRequest>(json, JsonOptions));
    }

    [Test]
    public void ChangeSpaceQuotaRequest_ShouldAcceptExplicitNull()
    {
        var request = JsonSerializer.Deserialize<ChangeSpaceQuotaRequest>(
            """{"allowedBytes":null}""", JsonOptions);

        Assert.That(request!.AllowedBytes, Is.Null);
    }

    [TestCase("""{}""")]
    [TestCase("""{"allowedBytes":"1024"}""")]
    public void ChangeSpaceQuotaRequest_ShouldRequireTheFieldAndRejectQuotedNumbers(string json)
    {
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<ChangeSpaceQuotaRequest>(json, JsonOptions));
    }

    private static JsonSerializerOptions CreateJsonOptions()
    {
        using var services = new ServiceCollection().AddCloudSharpHttp().BuildServiceProvider();
        return services.GetRequiredService<IOptions<JsonOptions>>().Value.SerializerOptions;
    }
}
