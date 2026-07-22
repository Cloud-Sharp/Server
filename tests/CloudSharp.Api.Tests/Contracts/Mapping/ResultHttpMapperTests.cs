using System.Net;
using System.Text.Json;
using CloudSharp.Api.Contracts.Mapping;
using CloudSharp.Api.Contracts.ProblemDetails;
using CloudSharp.Core.Common.Errors;
using FluentAssertions;
using FluentResults;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NUnit.Framework;

namespace CloudSharp.Api.Tests.Contracts.Mapping;

[TestFixture]
public class ResultHttpMapperTests
{
    private static readonly JsonSerializerOptions CamelCase = new(JsonSerializerDefaults.Web);

    private static async Task<(int StatusCode, ErrorResponse? Body, string RawJson)> ExecuteAsync(
        IResult result,
        string traceIdentifier,
        ILoggerFactory? loggerFactory = null)
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Response.Body = new MemoryStream();
        httpContext.TraceIdentifier = traceIdentifier;

        var services = new ServiceCollection();
        if (loggerFactory is not null)
        {
            services.AddSingleton(loggerFactory);
        }
        else
        {
            services.AddLogging();
        }
        httpContext.RequestServices = services.BuildServiceProvider();

        await result.ExecuteAsync(httpContext);

        httpContext.Response.Body.Position = 0;
        var rawJson = await new StreamReader(httpContext.Response.Body).ReadToEndAsync();
        var body = string.IsNullOrEmpty(rawJson)
            ? null
            : JsonSerializer.Deserialize<ErrorResponse>(rawJson, CamelCase);
        return (httpContext.Response.StatusCode, body, rawJson);
    }

    private sealed class TestCloudSharpError : CloudSharpError
    {
        public TestCloudSharpError(ErrorDefinition definition, string message)
            : base(definition, message)
        {
        }
    }

    private sealed class TestError : Error
    {
        public TestError(string message) : base(message)
        {
        }
    }

    [Test]
    public async Task ToHttpResult_WithSuccessValue_ShouldCallOnSuccessOnceWithValue()
    {
        var value = new SamplePayload("alpha");
        var result = Result.Ok<SamplePayload>(value);
        var calls = 0;
        SamplePayload? captured = null;

        var httpResult = result.ToHttpResult(v =>
        {
            calls++;
            captured = v;
            return Results.Ok(v);
        });

        calls.Should().Be(1);
        captured.Should().Be(value);

        var (status, body, _) = await ExecuteAsync(httpResult, "trace-1");
        status.Should().Be((int)HttpStatusCode.OK);
        body.Should().NotBeNull();
    }

    [Test]
    public async Task ToHttpResult_WithSuccessNoValue_ShouldReturnSuccessResult()
    {
        var result = Result.Ok();
        var httpResult = result.ToHttpResult(() => Results.NoContent());

        var (status, body, rawJson) = await ExecuteAsync(httpResult, "trace-2");
        status.Should().Be((int)HttpStatusCode.NoContent);
        body.Should().BeNull();
        rawJson.Should().BeEmpty();
    }

    [Test]
    public async Task ToHttpResult_WithFailure_ShouldNotCallOnSuccess()
    {
        var result = Result.Fail<SamplePayload>(new TestError("fail"));
        var calls = 0;

        var httpResult = result.ToHttpResult(_ =>
        {
            calls++;
            return Results.Ok();
        });

        calls.Should().Be(0);
        var (status, body, _) = await ExecuteAsync(httpResult, "trace-3");
        status.Should().Be((int)HttpStatusCode.InternalServerError);
        body.Should().NotBeNull();
    }

    [Test]
    public async Task ToHttpResult_WithSingleBusinessError_ShouldMapStatusAndCodeAndEmptyDetails()
    {
        var definition = new ErrorDefinition("REQUEST_CONFLICT");
        var error = new TestCloudSharpError(definition, "A file or folder with the same name already exists.");
        var result = Result.Fail<SamplePayload>(error);

        var httpResult = result.ToHttpResult(_ => Results.Ok());

        var (status, body, rawJson) = await ExecuteAsync(httpResult, "req-1");

        status.Should().Be((int)HttpStatusCode.Conflict);
        body.Should().NotBeNull();
        body!.RequestId.Should().Be("req-1");
        body.Error.Code.Should().Be("REQUEST_CONFLICT");
        body.Error.Message.Should().Be("A file or folder with the same name already exists.");
        body.Error.Details.Should().BeEmpty();
        rawJson.Should().Contain("\"requestId\":\"req-1\"");
        rawJson.Should().Contain("\"code\":\"REQUEST_CONFLICT\"");
        rawJson.Should().Contain("\"details\":[]");
    }

    [TestCase("REQUEST_VALIDATION_FAILED", HttpStatusCode.BadRequest)]
    [TestCase("AUTH_TOKEN_REQUIRED", HttpStatusCode.Unauthorized)]
    [TestCase("AUTH_TOKEN_INVALID", HttpStatusCode.Unauthorized)]
    [TestCase("AUTH_TOKEN_TYPE_NOT_ALLOWED", HttpStatusCode.Unauthorized)]
    [TestCase("PERMISSION_DENIED", HttpStatusCode.Forbidden)]
    [TestCase("RESOURCE_NOT_FOUND", HttpStatusCode.NotFound)]
    [TestCase("METHOD_NOT_ALLOWED", HttpStatusCode.MethodNotAllowed)]
    [TestCase("REQUEST_CONFLICT", HttpStatusCode.Conflict)]
    [TestCase("IDEMPOTENCY_KEY_REUSED", HttpStatusCode.Conflict)]
    [TestCase("SECRET_RESPONSE_NOT_REPLAYABLE", HttpStatusCode.Conflict)]
    [TestCase("PRECONDITION_FAILED", HttpStatusCode.PreconditionFailed)]
    [TestCase("PRECONDITION_REQUIRED", (HttpStatusCode)428)]
    [TestCase("RATE_LIMITED", HttpStatusCode.TooManyRequests)]
    [TestCase("DEPENDENCY_UNAVAILABLE", HttpStatusCode.ServiceUnavailable)]
    public async Task ToHttpResult_WithCatalogErrorCode_ShouldUseMappedStatus(
        string errorCode,
        HttpStatusCode expectedStatus)
    {
        var error = new TestCloudSharpError(new ErrorDefinition(errorCode), "mapped message");
        var result = Result.Fail<SamplePayload>(error);

        var httpResult = result.ToHttpResult(_ => Results.Ok());
        var (status, body, _) = await ExecuteAsync(httpResult, "catalog-request");

        status.Should().Be((int)expectedStatus);
        body!.Error.Code.Should().Be(errorCode);
        body.Error.Message.Should().Be("mapped message");
    }

    [Test]
    public async Task ToHttpResult_WithMultipleBusinessErrors_ShouldUseFirstErrorAsRepresentative()
    {
        var first = new TestCloudSharpError(
            new ErrorDefinition("REQUEST_CONFLICT"),
            "first message");
        var second = new TestCloudSharpError(
            new ErrorDefinition("RESOURCE_NOT_FOUND"),
            "second message");
        var result = Result.Fail<SamplePayload>(new[] { first, second });

        var httpResult = result.ToHttpResult(_ => Results.Ok());

        var (status, body, _) = await ExecuteAsync(httpResult, "req-2");

        status.Should().Be((int)HttpStatusCode.Conflict);
        body!.Error.Code.Should().Be("REQUEST_CONFLICT");
        body.Error.Message.Should().Be("first message");
        body.Error.Details.Should().BeEmpty();
    }

    [Test]
    public async Task ToHttpResult_WithMultipleValidationErrors_ShouldAggregateIntoSingle400WithCamelCaseFields()
    {
        var error1 = new Error("Name is required")
            .WithMetadata("ErrorCode", "NAME_REQUIRED")
            .WithMetadata("PropertyName", "Name")
            .WithMetadata("AttemptedValue", null);
        var error2 = new Error("Address Street is invalid")
            .WithMetadata("ErrorCode", "ADDRESS_STREET_INVALID")
            .WithMetadata("PropertyName", "Address.Street")
            .WithMetadata("AttemptedValue", "  ");
        var result = Result.Fail<SamplePayload>(new[] { error1, error2 });

        var httpResult = result.ToHttpResult(_ => Results.Ok());

        var (status, body, rawJson) = await ExecuteAsync(httpResult, "req-3");

        status.Should().Be((int)HttpStatusCode.BadRequest);
        body!.RequestId.Should().Be("req-3");
        body.Error.Code.Should().Be("REQUEST_VALIDATION_FAILED");
        body.Error.Message.Should().Be("One or more validation errors occurred.");
        body.Error.Details.Should().HaveCount(2);
        body.Error.Details[0].Field.Should().Be("name");
        body.Error.Details[0].Code.Should().Be("NAME_REQUIRED");
        body.Error.Details[1].Field.Should().Be("address.street");
        body.Error.Details[1].Code.Should().Be("ADDRESS_STREET_INVALID");
        rawJson.Should().NotContain("AttemptedValue");
        rawJson.Should().NotContain("  ");
    }

    [Test]
    public async Task ToHttpResult_WithMissingMetadata_ShouldReturnSafe500WithoutExposingErrorContent()
    {
        var error = new TestError("secret internal detail");
        var result = Result.Fail<SamplePayload>(error);

        var httpResult = result.ToHttpResult(_ => Results.Ok());

        var (status, body, rawJson) = await ExecuteAsync(httpResult, "req-4");

        status.Should().Be((int)HttpStatusCode.InternalServerError);
        body!.RequestId.Should().Be("req-4");
        body.Error.Code.Should().Be("INTERNAL_SERVER_ERROR");
        body.Error.Message.Should().Be("An unexpected error occurred.");
        body.Error.Details.Should().BeEmpty();
        rawJson.Should().NotContain("secret internal detail");
    }

    [Test]
    public async Task ToHttpResult_WithUnmappedErrorCode_ShouldReturnSafe500AndLogStructuredError()
    {
        var error = new Error("bad")
            .WithMetadata("ErrorCode", "UNMAPPED_ERROR");
        var result = Result.Fail<SamplePayload>(error);

        var loggerProvider = new CapturingLoggerProvider();
        var loggerFactory = new LoggerFactory(new ILoggerProvider[] { loggerProvider });

        var httpResult = result.ToHttpResult(_ => Results.Ok());

        var (status, body, rawJson) = await ExecuteAsync(httpResult, "req-5", loggerFactory);

        status.Should().Be((int)HttpStatusCode.InternalServerError);
        body!.Error.Code.Should().Be("INTERNAL_SERVER_ERROR");
        body.Error.Message.Should().Be("An unexpected error occurred.");
        rawJson.Should().NotContain("UNMAPPED_ERROR");
        rawJson.Should().NotContain("bad");

        loggerProvider.Entries.Should().Contain(e =>
            e.Level == LogLevel.Error &&
            e.Message.Contains("ErrorCount=1") &&
            e.Message.Contains("TraceId=req-5"));
        loggerProvider.Entries.Should().NotContain(e =>
            e.Message.Contains("UNMAPPED_ERROR") || e.Message.Contains("bad"));
    }

    private sealed record SamplePayload(string Name);

    private sealed class CapturingLoggerProvider : ILoggerProvider
    {
        public List<(string Category, LogLevel Level, string Message)> Entries { get; } = new();

        public ILogger CreateLogger(string categoryName) => new CapturingLogger(this, categoryName);

        public void Dispose() { }

        private sealed class CapturingLogger : ILogger
        {
            private readonly CapturingLoggerProvider _provider;
            private readonly string _category;

            public CapturingLogger(CapturingLoggerProvider provider, string category)
            {
                _provider = provider;
                _category = category;
            }

            public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(
                LogLevel logLevel,
                EventId eventId,
                TState state,
                Exception? exception,
                Func<TState, Exception?, string> formatter)
            {
                _provider.Entries.Add((_category, logLevel, formatter(state, exception)));
            }
        }

        private sealed class NullScope : IDisposable
        {
            public static readonly NullScope Instance = new();
            public void Dispose() { }
        }
    }
}
