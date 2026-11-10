using CloudSharp.Core.Abstractions.Transactions;
using FluentResults;

namespace CloudSharp.TestSupport.Fakes;

public sealed class FakeSpaceUnitOfWork(
    FakeSpaceRepository? spaceRepository = null,
    FakeSpaceBootstrapWriter? bootstrapWriter = null) : IUnitOfWork
{
    private readonly List<CancellationToken> capturedTokens = new();

    public int SaveChangesCallCount => capturedTokens.Count;

    public Func<int, IError?>? SaveChangesFailureFactory { get; set; }

    public Task<Result<int>> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        capturedTokens.Add(cancellationToken);
        var failure = SaveChangesFailureFactory?.Invoke(SaveChangesCallCount);
        if (failure is not null)
        {
            return Task.FromResult(Result.Fail<int>(failure));
        }

        var changeCount = 0;
        if (spaceRepository is not null)
        {
            var spaces = spaceRepository.Flush();
            if (spaces.IsFailed)
            {
                return Task.FromResult(Result.Fail<int>(spaces.Errors));
            }

            changeCount += spaces.Value;
        }

        if (bootstrapWriter is not null)
        {
            changeCount += bootstrapWriter.Flush();
        }

        return Task.FromResult(Result.Ok(changeCount));
    }
}
