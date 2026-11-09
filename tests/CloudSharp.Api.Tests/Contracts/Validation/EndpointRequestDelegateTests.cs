using System.Text;
using System.Text.Json;
using CloudSharp.Api.Auth.CurrentUser;
using CloudSharp.Api.Contracts.ProblemDetails;
using CloudSharp.Api.DependencyInjection;
using CloudSharp.Api.Endpoints.Auth;
using CloudSharp.Api.Endpoints.Users;
using CloudSharp.Api.Middleware;
using CloudSharp.Api.Tests.TestSupport;
using CloudSharp.Core.Abstractions.Auth;
using CloudSharp.Core.Abstractions.Persistence;
using CloudSharp.Core.Abstractions.Transactions;
using CloudSharp.Core.Common.Actors;
using CloudSharp.Core.Common.Time;
using CloudSharp.Core.Common.Tokens;
using CloudSharp.Core.UseCases.Auth.Logins;
using CloudSharp.Core.UseCases.Auth.PasswordChanges;
using CloudSharp.Core.UseCases.Auth.Registrations;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using NUnit.Framework;

namespace CloudSharp.Api.Tests.Contracts.Validation;

/// <summary>Exercises real route delegates and production HTTP options without starting a server.</summary>
[TestFixture]
public class EndpointRequestDelegateTests
{
    private const string Registrations = "/api/v2/auth/registrations";
    private const string Sessions = "/api/v2/auth/sessions";
    private const string PasswordChange = "/api/v2/me/password";
    private const string RegistrationPassword = "registration-password";
    private const string CurrentPassword = "example-password";
    private const string WrongLoginPassword = "wrong-login-password";
    private const string NewPassword = "new-example-password";
    private const string SessionTokenHash = "session-hash-value";
    private const string DecoratorCategoryPrefix = "CloudSharp.Api.Observability.LoggingUseCaseDecorator";

    [TestCase("{")]
    [TestCase("{\"email\":\"user@example.com\",\"password\":\"example-password\",\"unknown\":true}")]
    [TestCase("{\"email\":42,\"password\":\"example-password\"}")]
    [TestCase("null")]
    public async Task InvokeAsync_ShouldReturnStandardBindingError(string json)
    {
        var harness = new ApiTestHarness();
        var loggerProvider = new CapturingLoggerProvider();

        var context = await ExecuteRequestAsync(harness, Registrations, json, loggerProvider: loggerProvider);
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
        AssertNoCompletionEntries(loggerProvider);
    }

    [TestCase(Registrations, "{\"email\":\"invalid-email\",\"password\":\"example-password\"}", "email")]
    [TestCase(Registrations, "{\"email\":\"user@example.com\",\"password\":\"short\"}", "password")]
    [TestCase(PasswordChange, "{\"currentPassword\":\"current-password\",\"newPassword\":\"short\"}", "newPassword")]
    public async Task InvokeAsync_ShouldValidateBeforeCallingUseCase(string path, string json, string field)
    {
        var harness = new ApiTestHarness();
        var loggerProvider = new CapturingLoggerProvider();

        var context = await ExecuteRequestAsync(harness, path, json, loggerProvider: loggerProvider);
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
        AssertNoCompletionEntries(loggerProvider);
    }

    [Test]
    public async Task InvokeAsync_ShouldLogSuccessfulRegistration()
    {
        var harness = new ApiTestHarness();
        var loggerProvider = new CapturingLoggerProvider();
        const string json = $$"""
            {"email":"user@example.com","userName":"cloud-user","displayName":"Cloud User","password":"{{RegistrationPassword}}"}
            """;

        var context = await ExecuteRequestAsync(harness, Registrations, json, loggerProvider: loggerProvider);

        var entry = GetSingleCompletion(loggerProvider, nameof(RegistrationUseCase), "Succeeded");
        var issuedToken = harness.TokenIssuer.IssuedTokens.Single();
        Assert.Multiple(() =>
        {
            Assert.That(context.Response.StatusCode, Is.EqualTo(StatusCodes.Status201Created));
            Assert.That(context.Response.Headers.Location.ToString(), Is.EqualTo("/api/v2/me"));
            Assert.That(context.Response.Headers.CacheControl.ToString(), Is.EqualTo("no-store"));
            Assert.That(entry.State["ErrorCodes"], Is.Empty);
        });
        AssertNoSensitiveLogData(
            loggerProvider,
            RegistrationPassword,
            issuedToken.PlainToken,
            issuedToken.HashedToken);
    }

    [Test]
    public async Task InvokeAsync_ShouldLogSuccessfulLogin()
    {
        var harness = new ApiTestHarness();
        harness.SeedUser(password: CurrentPassword);
        var loggerProvider = new CapturingLoggerProvider();
        const string json = $$"""
            {"loginId":"user@example.com","password":"{{CurrentPassword}}"}
            """;

        var context = await ExecuteRequestAsync(harness, Sessions, json, loggerProvider: loggerProvider);

        var entry = GetSingleCompletion(loggerProvider, nameof(LoginUseCase), "Succeeded");
        var issuedToken = harness.TokenIssuer.IssuedTokens.Single();
        Assert.Multiple(() =>
        {
            Assert.That(context.Response.StatusCode, Is.EqualTo(StatusCodes.Status200OK));
            Assert.That(context.Response.Headers.CacheControl.ToString(), Is.EqualTo("no-store"));
            Assert.That(entry.State["ErrorCodes"], Is.Empty);
        });
        AssertNoSensitiveLogData(
            loggerProvider,
            CurrentPassword,
            issuedToken.PlainToken,
            issuedToken.HashedToken);
    }

    [Test]
    public async Task InvokeAsync_ShouldLogSuccessfulPasswordChange()
    {
        var harness = new ApiTestHarness();
        var user = harness.SeedUser(password: CurrentPassword);
        var session = harness.SeedUserSession(user, TokenHash.Create(SessionTokenHash).Value);
        var loggerProvider = new CapturingLoggerProvider();
        const string json = $$"""
            {"currentPassword":"{{CurrentPassword}}","newPassword":"{{NewPassword}}"}
            """;

        var context = await ExecuteRequestAsync(
            harness,
            PasswordChange,
            json,
            loggerProvider: loggerProvider,
            configureRequest: (services, requestContext) =>
            {
                services.GetRequiredService<ICurrentActorAccessor>().Set(
                    CurrentActor.ForSession(user.Id, user.PublicId, user.SystemRole, session.SessionId),
                    session.TokenHash.Value);
                requestContext.Request.Headers["If-Match"] = "\"v3\"";
            });

        var entry = GetSingleCompletion(loggerProvider, nameof(PasswordChangeUseCase), "Succeeded");
        Assert.Multiple(() =>
        {
            Assert.That(context.Response.StatusCode, Is.EqualTo(StatusCodes.Status204NoContent));
            Assert.That(context.Response.Headers.ETag.ToString(), Is.EqualTo("\"v4\""));
            Assert.That(context.Response.Headers.CacheControl.ToString(), Is.EqualTo("no-store"));
            Assert.That(entry.State["ErrorCodes"], Is.Empty);
        });
        AssertNoSensitiveLogData(loggerProvider, CurrentPassword, NewPassword, session.TokenHash.Value);
    }

    [Test]
    public async Task InvokeAsync_ShouldPreserveLoginCredentialFailure()
    {
        var harness = new ApiTestHarness();
        harness.SeedUser(password: CurrentPassword);
        var loggerProvider = new CapturingLoggerProvider();
        const string json = $$"""
            {"loginId":"user@example.com","password":"{{WrongLoginPassword}}"}
            """;

        var context = await ExecuteRequestAsync(harness, Sessions, json, loggerProvider: loggerProvider);
        var (error, _) = await ReadErrorAsync(context);

        var entry = GetSingleCompletion(
            loggerProvider,
            nameof(LoginUseCase),
            "Failed",
            "AUTH_INVALID_CREDENTIALS");
        Assert.Multiple(() =>
        {
            Assert.That(context.Response.StatusCode, Is.EqualTo(StatusCodes.Status401Unauthorized));
            Assert.That(context.Response.Headers.CacheControl.ToString(), Is.EqualTo("no-store"));
            Assert.That(error.Error.Code, Is.EqualTo("AUTH_INVALID_CREDENTIALS"));
            Assert.That(harness.TokenIssuer.IssuedTokens, Is.Empty);
            Assert.That(entry.State["UseCase"], Is.EqualTo(nameof(LoginUseCase)));
        });
        AssertNoSensitiveLogData(loggerProvider, CurrentPassword, WrongLoginPassword);
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
        string contentType = "application/json",
        CapturingLoggerProvider? loggerProvider = null,
        Action<IServiceProvider, DefaultHttpContext>? configureRequest = null)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Production" });
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["TokenHashing:ActiveKey"] = Convert.ToBase64String(new byte[32]),
            ["ConnectionStrings:PostgreSql"] = "Host=localhost;Database=cloudsharp_test;Username=test;Password=test",
            ["ConnectionStrings:Redis"] = "localhost:6379",
        });
        builder.Logging.ClearProviders();
        builder.Logging.SetMinimumLevel(LogLevel.Debug);
        builder.Logging.AddFilter(DecoratorCategoryPrefix, LogLevel.Debug);
        builder.Logging.AddProvider(loggerProvider ?? new CapturingLoggerProvider());
        builder.Services.AddCloudSharpApplication(builder.Configuration);
        builder.Services.AddCloudSharpHttp();
        builder.Services.Replace(ServiceDescriptor.Singleton<IClock>(harness.Clock));
        builder.Services.Replace(ServiceDescriptor.Singleton<IUserRepository>(harness.UserRepository));
        builder.Services.Replace(ServiceDescriptor.Singleton<IUnitOfWork>(harness.UnitOfWork));
        builder.Services.Replace(ServiceDescriptor.Singleton<ITransactionExecutor>(harness.TransactionExecutor));
        builder.Services.Replace(ServiceDescriptor.Singleton<ISessionStore>(harness.SessionStore));
        builder.Services.Replace(ServiceDescriptor.Singleton<ITokenIssuer>(harness.TokenIssuer));
        builder.Services.Replace(ServiceDescriptor.Singleton<IPasswordHasher>(harness.PasswordHasher));
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
        configureRequest?.Invoke(scope.ServiceProvider, context);

        var errorMiddleware = new ErrorHandlingMiddleware(endpoint.RequestDelegate!);
        var headersMiddleware = new NoStoreResponseMiddleware(ctx =>
            errorMiddleware.InvokeAsync(ctx, scope.ServiceProvider.GetRequiredService<ILogger<ErrorHandlingMiddleware>>()));
        await headersMiddleware.InvokeAsync(context);
        return context;
    }

    private static CapturedLogEntry GetSingleCompletion(
        CapturingLoggerProvider loggerProvider,
        string useCase,
        string outcome,
        params string[] errorCodes)
    {
        var entries = GetCompletionEntries(loggerProvider);
        Assert.That(entries, Has.Length.EqualTo(1));
        var entry = entries[0];
        Assert.Multiple(() =>
        {
            Assert.That(entry.Level, Is.EqualTo(LogLevel.Debug));
            Assert.That(entry.State["UseCase"], Is.EqualTo(useCase));
            Assert.That(entry.State["Outcome"], Is.EqualTo(outcome));
            Assert.That(entry.State["ErrorCodes"], Is.EqualTo(errorCodes));
        });
        return entry;
    }

    private static void AssertNoCompletionEntries(CapturingLoggerProvider loggerProvider) =>
        Assert.That(GetCompletionEntries(loggerProvider), Is.Empty);

    private static CapturedLogEntry[] GetCompletionEntries(CapturingLoggerProvider loggerProvider) =>
        loggerProvider.Entries
            .Where(entry => entry.CategoryName.StartsWith(DecoratorCategoryPrefix, StringComparison.Ordinal))
            .ToArray();

    private static void AssertNoSensitiveLogData(
        CapturingLoggerProvider loggerProvider,
        params string[] secrets)
    {
        foreach (var entry in GetCompletionEntries(loggerProvider))
        {
            var structuredState = JsonSerializer.Serialize(entry.State);
            foreach (var secret in secrets)
            {
                Assert.That(entry.RenderedMessage, Does.Not.Contain(secret));
                Assert.That(structuredState, Does.Not.Contain(secret));
            }
        }
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

    private sealed class CapturingLoggerProvider : ILoggerProvider
    {
        private readonly List<CapturedLogEntry> _entries = [];

        public IReadOnlyList<CapturedLogEntry> Entries => _entries;

        public ILogger CreateLogger(string categoryName) => new CapturingLogger(categoryName, _entries);

        public void Dispose()
        {
        }
    }

    private sealed class CapturingLogger(string categoryName, List<CapturedLogEntry> entries) : ILogger
    {
        public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;

        public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            var fields = state is IEnumerable<KeyValuePair<string, object?>> pairs
                ? pairs.ToDictionary(pair => pair.Key, pair => pair.Value)
                : throw new InvalidOperationException("Expected structured log state.");
            entries.Add(new CapturedLogEntry(
                categoryName,
                logLevel,
                formatter(state, exception),
                fields));
        }
    }

    private sealed record CapturedLogEntry(
        string CategoryName,
        LogLevel Level,
        string RenderedMessage,
        IReadOnlyDictionary<string, object?> State);

    private sealed class NullScope : IDisposable
    {
        public static readonly NullScope Instance = new();

        public void Dispose()
        {
        }
    }
}
