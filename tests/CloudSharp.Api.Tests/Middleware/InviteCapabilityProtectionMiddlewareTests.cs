using System.Diagnostics;
using CloudSharp.Api.Middleware;
using CloudSharp.Api.Observability;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NUnit.Framework;

namespace CloudSharp.Api.Tests.Middleware;

[TestFixture]
public sealed class InviteCapabilityProtectionMiddlewareTests
{
    [TestCase("GET", "/public/v2/invites/{inviteToken}")]
    [TestCase("POST", "/api/v2/invites/{inviteToken}/accept")]
    public async Task InvokeAsync_ShouldUseTheRouteTemplateInAccessLogsAndTracing(string method, string routeTemplate)
    {
        var token = "cs_inv_" + new string('A', 43);
        var path = routeTemplate.Replace("{inviteToken}", token, StringComparison.Ordinal);
        var url = $"https://example.test{path}";
        using var activity = new Activity($"{method} {path}").Start();
        activity.SetTag("http.target", path);
        activity.SetTag("http.url", url);
        activity.SetTag("url.full", url);
        activity.SetTag("url.path", path);
        var context = new DefaultHttpContext();
        var responseFeature = new CompletingResponseFeature();
        context.Features.Set<IHttpResponseFeature>(responseFeature);
        context.Request.Scheme = "https";
        context.Request.Method = method;
        context.Request.Path = path;
        var logger = new RecordingLogger();
        var middleware = new InviteCapabilityProtectionMiddleware(requestContext =>
        {
            requestContext.Response.StatusCode = StatusCodes.Status204NoContent;
            return Task.CompletedTask;
        });

        await middleware.InvokeAsync(context, logger);
        await responseFeature.CompleteAsync();

        Assert.Multiple(() =>
        {
            Assert.That(activity.GetTagItem("http.route") is string route && route == routeTemplate, Is.True);
            Assert.That(activity.DisplayName.Contains(routeTemplate, StringComparison.Ordinal), Is.True);
            Assert.That(activity.DisplayName.Contains(token, StringComparison.Ordinal), Is.False);
            Assert.That(activity.TagObjects.Any(tag => tag.Value is string value && value.Contains(token, StringComparison.Ordinal)), Is.False);
            Assert.That(logger.Messages.Any(message => message.Contains(routeTemplate, StringComparison.Ordinal)
                && message.Contains("204", StringComparison.Ordinal)), Is.True);
            Assert.That(logger.Messages.Any(message => message.Contains(token, StringComparison.Ordinal)), Is.False);
        });
    }

    [TestCase("/public/v2/invites/example")]
    [TestCase("/api/v2/invites/example/accept")]
    public void LogProtection_ShouldSuppressCapabilityPathCategoriesAndPreserveConfiguredFilters(string invitePath)
    {
        const string hostingCategory = "Microsoft.AspNetCore.Hosting.Diagnostics";
        const string matcherCategory = "Microsoft.AspNetCore.Routing.Matching.DfaMatcher";
        const string applicationCategory = "CloudSharp.Api.InviteDiagnostics";
        var recordingLogger = new RecordingLogger();
        var services = new ServiceCollection();
        services.AddLogging(logging =>
        {
            logging.AddProvider(new RecordingLoggerProvider(recordingLogger));
            logging.SetMinimumLevel(LogLevel.Warning);
            logging.AddFilter("Microsoft.AspNetCore", LogLevel.Debug);
            logging.AddFilter<RecordingLoggerProvider>(matcherCategory, level => level == LogLevel.Debug);
            logging.AddFilter<RecordingLoggerProvider>(applicationCategory, LogLevel.Debug);
        });
        services.AddInviteCapabilityLogProtection();
        using var provider = services.BuildServiceProvider();
        var accessor = provider.GetRequiredService<IHttpContextAccessor>();
        var loggerFactory = provider.GetRequiredService<ILoggerFactory>();
        var hosting = loggerFactory.CreateLogger(hostingCategory);
        var matcher = loggerFactory.CreateLogger(matcherCategory);
        var application = loggerFactory.CreateLogger(applicationCategory);
        var unconfigured = loggerFactory.CreateLogger("Unconfigured");

        try
        {
            var context = new DefaultHttpContext();
            accessor.HttpContext = context;
            context.Request.Path = "/api/v2/spaces";
            hosting.LogDebug("Regular hosting debug");
            hosting.LogTrace("Below category minimum");
            matcher.LogDebug("Regular matching debug");
            matcher.LogInformation("Rejected by provider predicate");

            context.Request.Path = invitePath;
            hosting.LogDebug("Capability hosting debug");
            hosting.LogWarning("Capability hosting warning");
            matcher.LogDebug("Capability matching debug");
            application.LogDebug("Application debug");
            application.LogTrace("Below provider minimum");
            unconfigured.LogInformation("Below default minimum");
            unconfigured.LogWarning("Default warning");

            accessor.HttpContext = null;
            hosting.LogDebug("Hosting debug without a request");
        }
        finally
        {
            accessor.HttpContext = null;
        }

        Assert.That(recordingLogger.Messages, Is.EqualTo(new[]
        {
            "Regular hosting debug",
            "Regular matching debug",
            "Application debug",
            "Default warning",
            "Hosting debug without a request",
        }));
    }

    private sealed class CompletingResponseFeature : HttpResponseFeature
    {
        private Func<object, Task>? _callback;
        private object? _state;

        public override void OnCompleted(Func<object, Task> callback, object state)
        {
            _callback = callback;
            _state = state;
        }

        internal Task CompleteAsync() => _callback is null ? Task.CompletedTask : _callback(_state!);
    }

    private sealed class RecordingLoggerProvider(RecordingLogger logger) : ILoggerProvider
    {
        public ILogger CreateLogger(string categoryName) => logger;

        public void Dispose() { }
    }

    private sealed class RecordingLogger : ILogger<InviteCapabilityProtectionMiddleware>
    {
        internal List<string> Messages { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) => Messages.Add(formatter(state, exception));
    }
}
