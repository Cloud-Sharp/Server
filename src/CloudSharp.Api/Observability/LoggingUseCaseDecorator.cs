using System.Diagnostics;
using CloudSharp.Core.Common.Errors;
using CloudSharp.Core.UseCases;
using FluentResults;
using Microsoft.Extensions.Logging;

namespace CloudSharp.Api.Observability;

internal sealed class LoggingUseCaseDecorator<TInput, TOutput>(
    IUseCase<TInput, TOutput> inner,
    ILogger<LoggingUseCaseDecorator<TInput, TOutput>> logger)
    : IUseCase<TInput, TOutput>
{
    private const string MessageTemplate =
        "Use case {UseCase} completed with {Outcome} in {ElapsedMs} ms. TraceId={TraceId}; ErrorCodes={ErrorCodes}; ExceptionType={ExceptionType}";

    public Task<Result<TOutput>> ExecuteAsync(
        TInput input,
        CancellationToken cancellationToken = default) =>
        ExecuteLoggedAsync(inner.GetType().Name, ct => inner.ExecuteAsync(input, ct), logger, cancellationToken);

    internal static async Task<Result<TExecutionOutput>> ExecuteLoggedAsync<TExecutionOutput>(
        string useCaseName,
        Func<CancellationToken, Task<Result<TExecutionOutput>>> execute,
        ILogger<LoggingUseCaseDecorator<TInput, TOutput>> logger,
        CancellationToken cancellationToken)
    {
        var startTimestamp = Stopwatch.GetTimestamp();
        Result<TExecutionOutput> result;

        try
        {
            result = await execute(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            TryLogCompletion(logger, useCaseName, startTimestamp, "Canceled");
            throw;
        }
        catch (Exception exception)
        {
            TryLogCompletion(logger, useCaseName, startTimestamp, "Faulted", exception: exception);
            throw;
        }

        TryLogCompletion(logger, useCaseName, startTimestamp, result.IsSuccess ? "Succeeded" : "Failed", result.Errors);
        return result;
    }

    private static void TryLogCompletion(
        ILogger logger,
        string useCaseName,
        long startTimestamp,
        string outcome,
        IReadOnlyList<IError>? errors = null,
        Exception? exception = null)
    {
        try
        {
            if (!logger.IsEnabled(LogLevel.Debug))
            {
                return;
            }

            var errorCodes = errors is { Count: > 0 }
                ? errors
                    .Select(error => error.Metadata.TryGetValue(
                        CloudSharpError.ErrorCodeMetadataKey,
                        out var value)
                        ? value
                        : null)
                    .OfType<string>()
                    .ToArray()
                : Array.Empty<string>();

            logger.Log(
                LogLevel.Debug,
                eventId: default,
                exception: null,
                message: MessageTemplate,
                args:
                [
                    useCaseName,
                    outcome,
                    Stopwatch.GetElapsedTime(startTimestamp).TotalMilliseconds,
                    Activity.Current?.TraceId.ToString(),
                    errorCodes,
                    exception?.GetType().Name,
                ]);
        }
        catch (Exception)
        {
        }
    }
}
