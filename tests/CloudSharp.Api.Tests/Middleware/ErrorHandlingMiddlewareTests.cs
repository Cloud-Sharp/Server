using System.Net;
using System.Text.Json;
using CloudSharp.Api.Contracts.ProblemDetails;
using CloudSharp.Api.Middleware;
using CloudSharp.Api.Tests.TestSupport;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using NUnit.Framework;

namespace CloudSharp.Api.Tests.Middleware;

[TestFixture]
public class ErrorHandlingMiddlewareTests
{
    [Test]
    public async Task InvokeAsync_WithBadHttpRequest_ShouldReturnStandard400()
    {
        var context = ApiTestHarness.CreateHttpContext("binding-request");
        var middleware = new ErrorHandlingMiddleware(
            _ => throw new BadHttpRequestException("raw body must not be exposed"));

        await middleware.InvokeAsync(context, LoggerFactory.Create(_ => { }).CreateLogger<ErrorHandlingMiddleware>());

        var error = await ReadErrorAsync(context);
        context.Response.StatusCode.Should().Be((int)HttpStatusCode.BadRequest);
        error.Error.Code.Should().Be("REQUEST_VALIDATION_FAILED");
        error.Error.Details.Should().BeEmpty();
    }

    [Test]
    public async Task InvokeAsync_WithUnexpectedException_ShouldReturnSafe500()
    {
        var context = ApiTestHarness.CreateHttpContext("unexpected-request");
        var middleware = new ErrorHandlingMiddleware(
            _ => throw new InvalidOperationException("secret exception detail"));

        await middleware.InvokeAsync(context, LoggerFactory.Create(_ => { }).CreateLogger<ErrorHandlingMiddleware>());

        var error = await ReadErrorAsync(context);
        context.Response.StatusCode.Should().Be((int)HttpStatusCode.InternalServerError);
        error.Error.Code.Should().Be("INTERNAL_SERVER_ERROR");
        error.Error.Message.Should().Be("An unexpected error occurred.");
        error.Error.Message.Should().NotContain("secret exception detail");
    }

    [Test]
    public void InvokeAsync_WithRequestAbortedCancellation_ShouldPropagate()
    {
        var context = ApiTestHarness.CreateHttpContext();
        using var cancellationSource = new CancellationTokenSource();
        cancellationSource.Cancel();
        context.RequestAborted = cancellationSource.Token;
        var middleware = new ErrorHandlingMiddleware(
            _ => throw new OperationCanceledException(cancellationSource.Token));

        Assert.ThrowsAsync<OperationCanceledException>(
            () => middleware.InvokeAsync(
                context,
                LoggerFactory.Create(_ => { }).CreateLogger<ErrorHandlingMiddleware>()));
    }

    private static async Task<ErrorResponse> ReadErrorAsync(HttpContext context)
    {
        context.Response.Body.Position = 0;
        var body = await new StreamReader(context.Response.Body).ReadToEndAsync();
        return JsonSerializer.Deserialize<ErrorResponse>(body, ApiTestHarness.CamelCaseJson)!;
    }
}
