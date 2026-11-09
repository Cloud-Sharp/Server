using FluentResults;

namespace CloudSharp.Core.UseCases;

public interface IUseCase<in TInput, TOutput>
{
    Task<Result<TOutput>> ExecuteAsync(TInput input, CancellationToken cancellationToken = default);
}
