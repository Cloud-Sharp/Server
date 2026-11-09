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

    public async Task<Result<TOutput>> ExecuteAsync(
        TInput input,
        CancellationToken cancellationToken = default)
    {
        var startTimestamp = Stopwatch.GetTimestamp();
        Result<TOutput> result;

        try
        {
            result = await inner.ExecuteAsync(input, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            TryLogCompletion(startTimestamp, "Canceled");
            throw;
        }
        catch (Exception exception)
        {
            TryLogCompletion(startTimestamp, "Faulted", exception: exception);
            throw;
        }

        TryLogCompletion(startTimestamp, result.IsSuccess ? "Succeeded" : "Failed", result);
        return result;
    }

    private void TryLogCompletion(
        long startTimestamp,
        string outcome,
        Result<TOutput>? result = null,
        Exception? exception = null)
    {
        try
        {
            if (!logger.IsEnabled(LogLevel.Debug))
            {
                return;
            }

            var errorCodes = result is { IsFailed: true }
                ? result.Errors
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
                    inner.GetType().Name,
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
