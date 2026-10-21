using CloudSharp.Api.Middleware;
using CloudSharp.Api.Tests.TestSupport;
using Microsoft.AspNetCore.Http;
using NUnit.Framework;

namespace CloudSharp.Api.Tests.Middleware;

[TestFixture]
public class NoStoreResponseMiddlewareTests
{
    [TestCase(StatusCodes.Status401Unauthorized)]
    [TestCase(StatusCodes.Status403Forbidden)]
    [TestCase(StatusCodes.Status503ServiceUnavailable)]
    public async Task InvokeAsync_ShouldProtectAuthenticationFailures(int statusCode)
    {
        var context = ApiTestHarness.CreateHttpContext();
        context.SetEndpoint(new Endpoint(null, new EndpointMetadataCollection(NoStoreResponseMetadata.Instance), "protected-auth"));
        var middleware = new NoStoreResponseMiddleware(ctx =>
        {
            Assert.That(ctx.Response.Headers.CacheControl.ToString(), Is.EqualTo("no-store"));
            ctx.Response.StatusCode = statusCode;
            return Task.CompletedTask;
        });

        await middleware.InvokeAsync(context);

        Assert.Multiple(() =>
        {
            Assert.That(context.Response.StatusCode, Is.EqualTo(statusCode));
            Assert.That(context.Response.Headers.CacheControl.ToString(), Is.EqualTo("no-store"));
        });
    }

    [Test]
    public async Task InvokeAsync_ShouldLeaveOtherEndpointCachePoliciesIntact()
    {
        var context = ApiTestHarness.CreateHttpContext();
        context.Response.Headers.CacheControl = "public, max-age=60";
        var middleware = new NoStoreResponseMiddleware(_ => Task.CompletedTask);

        await middleware.InvokeAsync(context);

        Assert.That(context.Response.Headers.CacheControl.ToString(), Is.EqualTo("public, max-age=60"));
    }
}
