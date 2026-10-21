using CloudSharp.Api.Auth.CurrentUser;
using CloudSharp.Api.Contracts.ProblemDetails;
using CloudSharp.Api.DependencyInjection;
using CloudSharp.Api.Endpoints.Auth;
using CloudSharp.Api.Endpoints.Users;
using CloudSharp.Api.Middleware;
using CloudSharp.Api.Tests.TestSupport;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NUnit.Framework;

namespace CloudSharp.Api.Tests.Endpoints;

[TestFixture]
public class AuthEndpointMetadataTests
{
    [TestCaseSource(nameof(AuthRoutes))]
    public async Task MapEndpoints_ShouldDescribeStandardJsonErrors(
        string method,
        string route,
        int[] expectedErrorStatuses)
    {
        var harness = new ApiTestHarness();
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = Environments.Production,
        });
        builder.Services.AddCloudSharpHttp();
        builder.Services.AddSingleton(harness.RegistrationUseCase);
        builder.Services.AddSingleton(harness.LoginUseCase);
        builder.Services.AddSingleton(harness.GetProfileUseCase);
        builder.Services.AddSingleton(harness.PasswordChangeUseCase);
        builder.Services.AddSingleton<ICurrentActorAccessor, CurrentActorAccessor>();
        await using var app = builder.Build();
        app.MapAuthEndpoints();
        app.MapUserEndpoints();

        var endpoint = ((IEndpointRouteBuilder)app).DataSources
            .SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>()
            .Single(candidate => candidate.RoutePattern.RawText?.TrimEnd('/') == route.TrimEnd('/')
                && candidate.Metadata.GetMetadata<HttpMethodMetadata>()!.HttpMethods.Contains(method));
        var errorResponses = endpoint.Metadata.GetOrderedMetadata<IProducesResponseTypeMetadata>()
            .Where(response => response.StatusCode >= 400)
            .ToArray();

        Assert.That(errorResponses.Select(response => response.StatusCode),
            Is.EquivalentTo(expectedErrorStatuses));
        foreach (var response in errorResponses)
        {
            Assert.That(response.Type, Is.EqualTo(typeof(ErrorResponse)));
            Assert.That(response.ContentTypes, Is.EqualTo(new[] { "application/json" }));
        }

        Assert.That(endpoint.Metadata.GetMetadata<IEndpointSummaryMetadata>()?.Summary,
            Is.Not.Null.And.Not.Empty);
        Assert.That(endpoint.Metadata.GetMetadata<NoStoreResponseMetadata>(), Is.Not.Null);
    }

    private static IEnumerable<TestCaseData> AuthRoutes()
    {
        yield return new TestCaseData("POST", "/api/v2/auth/registrations", new[] { 400, 409, 503 })
            .SetName("Registration documents standard JSON errors");
        yield return new TestCaseData("POST", "/api/v2/auth/sessions", new[] { 400, 401, 503 })
            .SetName("Login documents standard JSON errors");
        yield return new TestCaseData("GET", "/api/v2/me", new[] { 401, 503 })
            .SetName("Profile documents standard JSON errors");
        yield return new TestCaseData("PUT", "/api/v2/me/password", new[] { 400, 401, 412, 428, 503 })
            .SetName("Password change documents standard JSON errors");
    }
}
