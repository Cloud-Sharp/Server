using CloudSharp.Core.Common.Idempotency;

namespace CloudSharp.Core.UseCases.Idempotency.Execution;

public abstract record IdempotentExecutionResult<T>
{
    private IdempotentExecutionResult() { }

    public sealed record Executed(T Value) : IdempotentExecutionResult<T>;

    public sealed record Recovered(IdempotencyRecord.Completed Completion) : IdempotentExecutionResult<T>;
}
