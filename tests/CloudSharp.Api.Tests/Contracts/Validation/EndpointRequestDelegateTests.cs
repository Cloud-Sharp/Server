using System.Text;
using System.Text.Json;
using CloudSharp.Api.Auth.CurrentUser;
using CloudSharp.Api.Contracts.ProblemDetails;
using CloudSharp.Api.DependencyInjection;
using CloudSharp.Api.Endpoints.Auth;
using CloudSharp.Api.Endpoints.Users;
using CloudSharp.Api.Middleware;
using CloudSharp.Api.Tests.TestSupport;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NUnit.Framework;

namespace CloudSharp.Api.Tests.Contracts.Validation;

/// <summary>Exercises real route delegates and production HTTP options without starting a server.</summary>
[TestFixture]
public class EndpointRequestDelegateTests
{
    private const string Registrations = "/api/v2/auth/registrations";

    [TestCase("{")]
    [TestCase("{\"email\":\"user@example.com\",\"password\":\"example-password\",\"unknown\":true}")]
    [TestCase("{\"email\":42,\"password\":\"example-password\"}")]
    [TestCase("null")]
    public async Task InvokeAsync_ShouldReturnStandardBindingError(string json)
    {
        var harness = new ApiTestHarness();

        var context = await ExecuteRequestAsync(harness, Registrations, json);
        var (error, body) = await ReadErrorAsync(context);

        Assert.Multiple(() =>
        {
            Assert.That(context.Response.StatusCode, Is.EqualTo(StatusCodes.Status400BadRequest));
            Assert.That(context.Response.ContentType, Does.StartWith("application/json"));
            Assert.That(context.Response.Headers.CacheControl.ToString(), Is.EqualTo("no-store"));
            Assert.That(error.RequestId, Is.EqualTo("delegate-request"));
            Assert.That(error.Error.Code, Is.EqualTo("REQUEST_VALIDATION_FAILED"));
            Assert.That(error.Error.Details, Is.Empty);
            Assert.That(body, Does.Not.Contain("example-password"));
            Assert.That(harness.UserRepository.AddedUsers, Is.Empty);
            Assert.That(harness.TokenIssuer.IssuedTokens, Is.Empty);
        });
    }

    [TestCase(Registrations, "{\"email\":\"invalid-email\",\"password\":\"example-password\"}", "email")]
    [TestCase(Registrations, "{\"email\":\"user@example.com\",\"password\":\"short\"}", "password")]
    [TestCase("/api/v2/me/password", "{\"currentPassword\":\"current-password\",\"newPassword\":\"short\"}", "newPassword")]
    public async Task InvokeAsync_ShouldValidateBeforeCallingUseCase(string path, string json, string field)
    {
        var harness = new ApiTestHarness();

        var context = await ExecuteRequestAsync(harness, path, json);
        var (error, _) = await ReadErrorAsync(context);

        Assert.Multiple(() =>
        {
            Assert.That(context.Response.StatusCode, Is.EqualTo(StatusCodes.Status400BadRequest));
            Assert.That(context.Response.Headers.CacheControl.ToString(), Is.EqualTo("no-store"));
            Assert.That(error.Error.Code, Is.EqualTo("REQUEST_VALIDATION_FAILED"));
            // INVALID comes from automatic validation, rather than the Core validator's USER_* code.
            Assert.That(error.Error.Details.Any(detail => detail.Field == field && detail.Code == "INVALID"), Is.True);
            Assert.That(harness.PasswordHasher.HashedInputs, Is.Empty);
            Assert.That(harness.UserRepository.AddedUsers, Is.Empty);
        });
    }

    [Test]
    public async Task InvokeAsync_ShouldPreserveLoginCredentialFailure()
    {
        var harness = new ApiTestHarness();

        var context = await ExecuteRequestAsync(harness, "/api/v2/auth/sessions", "{}");
        var (error, _) = await ReadErrorAsync(context);

        Assert.Multiple(() =>
        {
            Assert.That(context.Response.StatusCode, Is.EqualTo(StatusCodes.Status401Unauthorized));
            Assert.That(context.Response.Headers.CacheControl.ToString(), Is.EqualTo("no-store"));
            Assert.That(error.Error.Code, Is.EqualTo("AUTH_INVALID_CREDENTIALS"));
            Assert.That(harness.TokenIssuer.IssuedTokens, Is.Empty);
        });
    }

    [Test]
    public async Task InvokeAsync_ShouldPreserveUnsupportedMediaTypeStatus()
    {
        var context = await ExecuteRequestAsync(new ApiTestHarness(), Registrations, "{}", "text/plain");
        var (error, _) = await ReadErrorAsync(context);

        Assert.Multiple(() =>
        {
            Assert.That(context.Response.StatusCode, Is.EqualTo(StatusCodes.Status415UnsupportedMediaType));
            Assert.That(context.Response.Headers.CacheControl.ToString(), Is.EqualTo("no-store"));
            Assert.That(error.Error.Code, Is.EqualTo("REQUEST_VALIDATION_FAILED"));
        });
    }

    private static async Task<DefaultHttpContext> ExecuteRequestAsync(
        ApiTestHarness harness,
        string path,
        string json,
        string contentType = "application/json")
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Production" });
        builder.Logging.ClearProviders();
        builder.Services.AddCloudSharpHttp();
        builder.Services.AddScoped(_ => harness.RegistrationUseCase);
        builder.Services.AddScoped(_ => harness.LoginUseCase);
        builder.Services.AddScoped(_ => harness.GetProfileUseCase);
        builder.Services.AddScoped(_ => harness.PasswordChangeUseCase);
        builder.Services.AddScoped<ICurrentActorAccessor, CurrentActorAccessor>();
        await using var app = builder.Build();
        app.MapAuthEndpoints();
        app.MapUserEndpoints();

        var endpoint = ((IEndpointRouteBuilder)app).DataSources
            .SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>()
            .Single(candidate => candidate.RoutePattern.RawText == path);
        using var scope = app.Services.CreateScope();
        var context = new DefaultHttpContext { RequestServices = scope.ServiceProvider, TraceIdentifier = "delegate-request" };
        context.SetEndpoint(endpoint);
        context.Request.Method = endpoint.Metadata.GetMetadata<HttpMethodMetadata>()!.HttpMethods[0];
        context.Request.Path = path;
        context.Request.ContentType = contentType;
        var bytes = Encoding.UTF8.GetBytes(json);
        context.Request.ContentLength = bytes.Length;
        context.Request.Body = new MemoryStream(bytes);
        context.Features.Set<IHttpRequestBodyDetectionFeature>(new BodyDetectionFeature());
        context.Response.Body = new MemoryStream();

        var errorMiddleware = new ErrorHandlingMiddleware(endpoint.RequestDelegate!);
        var headersMiddleware = new NoStoreResponseMiddleware(ctx =>
            errorMiddleware.InvokeAsync(ctx, scope.ServiceProvider.GetRequiredService<ILogger<ErrorHandlingMiddleware>>()));
        await headersMiddleware.InvokeAsync(context);
        return context;
    }

    private static async Task<(ErrorResponse Error, string Body)> ReadErrorAsync(HttpContext context)
    {
        context.Response.Body.Position = 0;
        var body = await new StreamReader(context.Response.Body).ReadToEndAsync();
        return (JsonSerializer.Deserialize<ErrorResponse>(body, ApiTestHarness.CamelCaseJson)!, body);
    }

    private sealed class BodyDetectionFeature : IHttpRequestBodyDetectionFeature
    {
        public bool CanHaveBody => true;
    }
}
