using System.Text.RegularExpressions;
using CloudSharp.Api.Middleware;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NUnit.Framework;

namespace CloudSharp.Api.Tests.Middleware;

[TestFixture]
public class CorrelationIdMiddlewareTests
{
    private const string HeaderName = CorrelationIdMiddleware.HeaderName;
    private const string LogScopeKey = CorrelationIdMiddleware.LogScopeKey;

    private static readonly Regex ServerIdPattern =
        new("^[0-9a-f]{32}$", RegexOptions.Compiled);

    private static async Task<(HttpContext Context, ScopeCapturingLogger Logger)> InvokeAsync(
        string? requestHeader,
        Action<HttpContext>? configureRequest = null)
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Response.Body = new MemoryStream();
        if (requestHeader is not null)
        {
            httpContext.Request.Headers[HeaderName] = requestHeader;
        }
        configureRequest?.Invoke(httpContext);

        var scopeProvider = new ScopeCapturingLoggerProvider();
        var loggerFactory = new LoggerFactory(new ILoggerProvider[] { scopeProvider });
        var logger = loggerFactory.CreateLogger<CorrelationIdMiddleware>();

        HttpContext? captured = null;
        var middleware = new CorrelationIdMiddleware(_ =>
        {
            captured = _;
            return Task.CompletedTask;
        });

        await middleware.InvokeAsync(httpContext, logger);

        captured.Should().NotBeNull();
        return (captured!, scopeProvider.Logger);
    }

    [Test]
    public async Task InvokeAsync_WithValidSingleHeader_ShouldPropagateToTraceIdentifierAndResponseAndLogScope()
    {
        var incoming = "abc-123.456:789_test";
        var (context, logger) = await InvokeAsync(incoming);

        context.TraceIdentifier.Should().Be(incoming);
        context.Response.Headers[HeaderName].ToString().Should().Be(incoming);

        logger.ScopeValues.Should().ContainKey(LogScopeKey)
            .WhoseValue.Should().Be(incoming);
    }

    [Test]
    public async Task InvokeAsync_WithoutHeader_ShouldGenerateUuidV7ServerId()
    {
        var (context, _) = await InvokeAsync(null);

        var id = context.TraceIdentifier;
        ServerIdPattern.IsMatch(id).Should().BeTrue(
            "서버 생성 ID는 UUIDv7의 32자리 hex 형식이어야 합니다, got {0}", id);

        context.Response.Headers[HeaderName].ToString().Should().Be(id);
    }

    [Test]
    public async Task InvokeAsync_WithoutHeader_ShouldGenerateUniqueIdsPerRequest()
    {
        var (_, first) = await InvokeAsync(null);
        var (_, second) = await InvokeAsync(null);

        var firstId = first.ScopeValues[LogScopeKey];
        var secondId = second.ScopeValues[LogScopeKey];

        firstId.Should().NotBe(secondId);
    }

    [TestCase("")]
    [TestCase("   ")]
    [TestCase("a b")]
    [TestCase("invalid!")]
    [TestCase("a/b")]
    public async Task InvokeAsync_WithInvalidHeaderValue_ShouldReplaceWithServerId(
        string headerValue)
    {
        var (context, _) = await InvokeAsync(headerValue);

        var id = context.TraceIdentifier;
        ServerIdPattern.IsMatch(id).Should().BeTrue(
            "잘못된 헤더는 서버 생성 ID로 대체되어야 합니다, got {0}", id);
        context.Response.Headers[HeaderName].ToString().Should().Be(id);
    }

    [Test]
    public async Task InvokeAsync_WithMultipleHeaderValues_ShouldReplaceWithServerId()
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Response.Body = new MemoryStream();
        httpContext.Request.Headers[HeaderName] = new[] { "one", "two" };

        var scopeProvider = new ScopeCapturingLoggerProvider();
        var loggerFactory = new LoggerFactory(new ILoggerProvider[] { scopeProvider });
        var logger = loggerFactory.CreateLogger<CorrelationIdMiddleware>();

        var middleware = new CorrelationIdMiddleware(_ => Task.CompletedTask);
        await middleware.InvokeAsync(httpContext, logger);

        var id = httpContext.TraceIdentifier;
        ServerIdPattern.IsMatch(id).Should().BeTrue(
            "복수 헤더는 서버 생성 ID로 대체되어야 합니다, got {0}", id);
    }

    [Test]
    public async Task InvokeAsync_WithHeaderExceedingMaxLength_ShouldReplaceWithServerId()
    {
        var tooLong = new string('a', CorrelationIdMiddleware.MaxLength + 1);

        var (context, _) = await InvokeAsync(tooLong);

        var id = context.TraceIdentifier;
        ServerIdPattern.IsMatch(id).Should().BeTrue(
            "길이 초과 헤더는 서버 생성 ID로 대체되어야 합니다, got {0}", id);
        context.Response.Headers[HeaderName].ToString().Should().Be(id);
    }

    [Test]
    public async Task InvokeAsync_WithMaxLengthHeader_ShouldPropagateAsIs()
    {
        var max = new string('a', CorrelationIdMiddleware.MaxLength);

        var (context, _) = await InvokeAsync(max);

        context.TraceIdentifier.Should().Be(max);
        context.Response.Headers[HeaderName].ToString().Should().Be(max);
    }

    [Test]
    public async Task InvokeAsync_WithSurroundingWhitespace_ShouldTrimAndPropagate()
    {
        var (context, _) = await InvokeAsync("  abc-123  ");

        context.TraceIdentifier.Should().Be("abc-123");
        context.Response.Headers[HeaderName].ToString().Should().Be("abc-123");
    }

    private sealed class ScopeCapturingLoggerProvider : ILoggerProvider
    {
        public ScopeCapturingLogger Logger { get; } = new();

        public ILogger CreateLogger(string categoryName) => Logger;

        public void Dispose() { }
    }

    private sealed class ScopeCapturingLogger : ILogger
    {
        public Dictionary<string, object?> ScopeValues { get; } = new();

        public IDisposable BeginScope<TState>(TState state) where TState : notnull
        {
            if (state is IEnumerable<KeyValuePair<string, object?>> pairs)
            {
                foreach (var (key, value) in pairs)
                {
                    ScopeValues[key] = value;
                }
            }

            return NullScope.Instance;
        }

        public bool IsEnabled(LogLevel logLevel) => false;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
        }

        private sealed class NullScope : IDisposable
        {
            public static readonly NullScope Instance = new();
            public void Dispose() { }
        }
    }
}