using System.Diagnostics;
using System.Text.Json;
using CloudSharp.Api.Observability;
using CloudSharp.Core.Common.Errors;
using CloudSharp.Core.UseCases;
using FluentResults;
using Microsoft.Extensions.Logging;
using NUnit.Framework;

namespace CloudSharp.Api.Tests.Observability;

[TestFixture]
public class LoggingUseCaseDecoratorTests
{
    private const string InputSecret = "input-secret-sentinel";
    private const string OutputSecret = "output-secret-sentinel";
    private const string ErrorSecret = "error-secret-sentinel";
    private const string ExceptionSecret = "exception-secret-sentinel";

    [Test]
    public async Task ExecuteAsync_ShouldReturnSuccessfulResultAndLogCompletion()
    {
        var input = new TestInput(InputSecret);
        var expected = Result.Ok(new TestOutput(OutputSecret));
        var useCase = new StubUseCase((_, _) => Task.FromResult(expected));
        var logger = new CapturingLogger();
        var decorator = CreateDecorator(useCase, logger);
        using var activity = new Activity("UseCaseTest").SetIdFormat(ActivityIdFormat.W3C);
        activity.Start();
        using var cancellationSource = new CancellationTokenSource();

        var actual = await decorator.ExecuteAsync(input, cancellationSource.Token);

        AssertOriginalInvocation(useCase, input, cancellationSource.Token);
        Assert.That(actual, Is.SameAs(expected));
        var entry = GetSingleEntry(logger);
        AssertCompletion(
            entry,
            outcome: "Succeeded",
            errorCodes: [],
            traceId: activity.TraceId.ToString(),
            exceptionType: null);
        AssertNoSecrets(entry);
    }

    [Test]
    public async Task ExecuteAsync_ShouldReturnFailedResultAndLogErrorCodes()
    {
        var input = new TestInput(InputSecret);
        var errors = new List<IError>
        {
            new Error(ErrorSecret).WithMetadata(CloudSharpError.ErrorCodeMetadataKey, "FIRST_ERROR"),
            new Error("Unrelated metadata").WithMetadata("Sensitive", OutputSecret),
            new Error("Non-string code").WithMetadata(CloudSharpError.ErrorCodeMetadataKey, 42),
            new Error("Second failure").WithMetadata(CloudSharpError.ErrorCodeMetadataKey, "SECOND_ERROR"),
        };
        var expected = Result.Fail<TestOutput>(errors);
        var useCase = new StubUseCase((_, _) => Task.FromResult(expected));
        var logger = new CapturingLogger();
        var decorator = CreateDecorator(useCase, logger);
        using var cancellationSource = new CancellationTokenSource();

        var actual = await decorator.ExecuteAsync(input, cancellationSource.Token);

        AssertOriginalInvocation(useCase, input, cancellationSource.Token);
        Assert.That(actual, Is.SameAs(expected));
        var entry = GetSingleEntry(logger);
        AssertCompletion(
            entry,
            outcome: "Failed",
            errorCodes: ["FIRST_ERROR", "SECOND_ERROR"],
            traceId: null,
            exceptionType: null);
        AssertNoSecrets(entry);
    }

    [Test]
    public async Task ExecuteAsync_ShouldPropagateCancellationAndLogCanceledOutcome()
    {
        var input = new TestInput(InputSecret);
        var expected = new OperationCanceledException("Use case canceled");
        var useCase = new StubUseCase((_, _) => Task.FromException<Result<TestOutput>>(expected));
        var logger = new CapturingLogger();
        var decorator = CreateDecorator(useCase, logger);
        using var cancellationSource = new CancellationTokenSource();
        cancellationSource.Cancel();

        var actual = Assert.ThrowsAsync<OperationCanceledException>(
            async () => await decorator.ExecuteAsync(input, cancellationSource.Token));

        AssertOriginalInvocation(useCase, input, cancellationSource.Token);
        Assert.That(actual, Is.SameAs(expected));
        var entry = GetSingleEntry(logger);
        AssertCompletion(
            entry,
            outcome: "Canceled",
            errorCodes: [],
            traceId: null,
            exceptionType: null);
        AssertNoSecrets(entry);
    }

    [Test]
    public async Task ExecuteAsync_ShouldPropagateExceptionAndLogFaultType()
    {
        var input = new TestInput(InputSecret);
        var expected = new InvalidOperationException(ExceptionSecret);
        var useCase = new StubUseCase((_, _) => Task.FromException<Result<TestOutput>>(expected));
        var logger = new CapturingLogger();
        var decorator = CreateDecorator(useCase, logger);
        using var cancellationSource = new CancellationTokenSource();

        var actual = Assert.ThrowsAsync<InvalidOperationException>(
            async () => await decorator.ExecuteAsync(input, cancellationSource.Token));

        AssertOriginalInvocation(useCase, input, cancellationSource.Token);
        Assert.That(actual, Is.SameAs(expected));
        var entry = GetSingleEntry(logger);
        AssertCompletion(
            entry,
            outcome: "Faulted",
            errorCodes: [],
            traceId: null,
            exceptionType: nameof(InvalidOperationException));
        AssertNoSecrets(entry);
    }

    [Test]
    public async Task ExecuteAsync_ShouldSkipLoggingForDisabledDebugLevel()
    {
        var input = new TestInput(InputSecret);
        var expected = Result.Ok(new TestOutput(OutputSecret));
        var useCase = new StubUseCase((_, _) => Task.FromResult(expected));
        var logger = new CapturingLogger { DebugEnabled = false };
        var decorator = CreateDecorator(useCase, logger);
        using var cancellationSource = new CancellationTokenSource();

        var actual = await decorator.ExecuteAsync(input, cancellationSource.Token);

        AssertOriginalInvocation(useCase, input, cancellationSource.Token);
        Assert.That(actual, Is.SameAs(expected));
        Assert.Multiple(() =>
        {
            Assert.That(logger.IsEnabledCallCount, Is.EqualTo(1));
            Assert.That(logger.LogCallCount, Is.Zero);
            Assert.That(logger.Entries, Is.Empty);
        });
    }

    [TestCase(true)]
    [TestCase(false)]
    public async Task ExecuteAsync_ShouldReturnResultWhenLoggingProviderThrows(bool isEnabledThrows)
    {
        var input = new TestInput(InputSecret);
        var expected = Result.Ok(new TestOutput(OutputSecret));
        var useCase = new StubUseCase((_, _) => Task.FromResult(expected));
        var providerException = new InvalidOperationException("Logging provider failed");
        var logger = CreateThrowingLogger(providerException, isEnabledThrows);
        var decorator = CreateDecorator(useCase, logger);
        using var cancellationSource = new CancellationTokenSource();

        var actual = await decorator.ExecuteAsync(input, cancellationSource.Token);

        AssertOriginalInvocation(useCase, input, cancellationSource.Token);
        Assert.That(actual, Is.SameAs(expected));
        AssertProviderFailureObserved(logger, isEnabledThrows);
    }

    [TestCase(true)]
    [TestCase(false)]
    public async Task ExecuteAsync_ShouldPropagateUseCaseExceptionWhenLoggingProviderThrows(
        bool isEnabledThrows)
    {
        var input = new TestInput(InputSecret);
        var expected = new InvalidOperationException(ExceptionSecret);
        var useCase = new StubUseCase((_, _) => Task.FromException<Result<TestOutput>>(expected));
        var providerException = new InvalidOperationException("Logging provider failed");
        var logger = CreateThrowingLogger(providerException, isEnabledThrows);
        var decorator = CreateDecorator(useCase, logger);
        using var cancellationSource = new CancellationTokenSource();

        var actual = Assert.ThrowsAsync<InvalidOperationException>(
            async () => await decorator.ExecuteAsync(input, cancellationSource.Token));

        AssertOriginalInvocation(useCase, input, cancellationSource.Token);
        Assert.That(actual, Is.SameAs(expected));
        AssertProviderFailureObserved(logger, isEnabledThrows);
    }

    private static LoggingUseCaseDecorator<TestInput, TestOutput> CreateDecorator(
        StubUseCase useCase,
        CapturingLogger logger) =>
        new(useCase, logger);

    private static CapturingLogger CreateThrowingLogger(
        Exception providerException,
        bool isEnabledThrows) =>
        new()
        {
            IsEnabledException = isEnabledThrows ? providerException : null,
            LogException = isEnabledThrows ? null : providerException,
        };

    private static void AssertOriginalInvocation(
        StubUseCase useCase,
        TestInput input,
        CancellationToken cancellationToken)
    {
        Assert.Multiple(() =>
        {
            Assert.That(useCase.CallCount, Is.EqualTo(1));
            Assert.That(useCase.Input, Is.SameAs(input));
            Assert.That(useCase.CancellationToken, Is.EqualTo(cancellationToken));
        });
    }

    private static LogEntry GetSingleEntry(CapturingLogger logger)
    {
        Assert.Multiple(() =>
        {
            Assert.That(logger.IsEnabledCallCount, Is.EqualTo(1));
            Assert.That(logger.LogCallCount, Is.EqualTo(1));
            Assert.That(logger.Entries, Has.Count.EqualTo(1));
        });
        return logger.Entries[0];
    }

    private static void AssertCompletion(
        LogEntry entry,
        string outcome,
        string[] errorCodes,
        string? traceId,
        string? exceptionType)
    {
        Assert.Multiple(() =>
        {
            Assert.That(entry.Level, Is.EqualTo(LogLevel.Debug));
            Assert.That(entry.Exception, Is.Null);
            Assert.That(entry.State.Keys, Is.EquivalentTo(new[]
            {
                "UseCase",
                "Outcome",
                "ElapsedMs",
                "TraceId",
                "ErrorCodes",
                "ExceptionType",
                "{OriginalFormat}",
            }));
            Assert.That(entry.State["UseCase"], Is.EqualTo(nameof(StubUseCase)));
            Assert.That(entry.State["Outcome"], Is.EqualTo(outcome));
            Assert.That(entry.State["ElapsedMs"], Is.GreaterThanOrEqualTo(0d));
            Assert.That(entry.State["TraceId"], Is.EqualTo(traceId));
            Assert.That(entry.State["ErrorCodes"], Is.EqualTo(errorCodes));
            Assert.That(entry.State["ExceptionType"], Is.EqualTo(exceptionType));
        });
    }

    private static void AssertNoSecrets(LogEntry entry)
    {
        var structuredState = JsonSerializer.Serialize(entry.State);
        var secrets = new[] { InputSecret, OutputSecret, ErrorSecret, ExceptionSecret };

        Assert.Multiple(() =>
        {
            foreach (var secret in secrets)
            {
                Assert.That(entry.RenderedMessage, Does.Not.Contain(secret));
                Assert.That(structuredState, Does.Not.Contain(secret));
            }
        });
    }

    private static void AssertProviderFailureObserved(
        CapturingLogger logger,
        bool isEnabledThrows)
    {
        Assert.Multiple(() =>
        {
            Assert.That(logger.IsEnabledCallCount, Is.EqualTo(1));
            Assert.That(logger.LogCallCount, Is.EqualTo(isEnabledThrows ? 0 : 1));
        });
    }

    private sealed record TestInput(string Secret);

    private sealed record TestOutput(string Secret);

    private sealed class StubUseCase(
        Func<TestInput, CancellationToken, Task<Result<TestOutput>>> execute)
        : IUseCase<TestInput, TestOutput>
    {
        public int CallCount { get; private set; }

        public TestInput? Input { get; private set; }

        public CancellationToken CancellationToken { get; private set; }

        public Task<Result<TestOutput>> ExecuteAsync(
            TestInput input,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            Input = input;
            CancellationToken = cancellationToken;
            return execute(input, cancellationToken);
        }
    }

    private sealed class CapturingLogger
        : ILogger<LoggingUseCaseDecorator<TestInput, TestOutput>>
    {
        public List<LogEntry> Entries { get; } = [];

        public bool DebugEnabled { get; init; } = true;

        public Exception? IsEnabledException { get; init; }

        public Exception? LogException { get; init; }

        public int IsEnabledCallCount { get; private set; }

        public int LogCallCount { get; private set; }

        public IDisposable BeginScope<TState>(TState state) where TState : notnull =>
            NullScope.Instance;

        public bool IsEnabled(LogLevel logLevel)
        {
            IsEnabledCallCount++;
            if (IsEnabledException is not null)
            {
                throw IsEnabledException;
            }

            return DebugEnabled && logLevel == LogLevel.Debug;
        }

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            LogCallCount++;
            if (LogException is not null)
            {
                throw LogException;
            }

            var fields = state is IEnumerable<KeyValuePair<string, object?>> pairs
                ? pairs.ToDictionary(pair => pair.Key, pair => pair.Value)
                : throw new InvalidOperationException("Expected structured log state.");
            Entries.Add(new LogEntry(
                logLevel,
                exception,
                formatter(state, exception),
                fields));
        }
    }

    private sealed record LogEntry(
        LogLevel Level,
        Exception? Exception,
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
