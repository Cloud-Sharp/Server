using CloudSharp.Core.Abstractions.Persistence;
using CloudSharp.Core.Common.Results;
using CloudSharp.Core.UseCases.Mounts.Dtos;
using FluentResults;

namespace CloudSharp.Core.UseCases.Mounts.Listings;

public sealed class ListMountsUseCase : IUseCase<ListMountsQuery, MountPageDto>
{
    private ISpaceAuthorizationReader AuthorizationReader { get; } = null!;
    private IMountListCandidateReader CandidateReader { get; } = null!;

    public ListMountsUseCase() { }

    internal ListMountsUseCase(
        ISpaceAuthorizationReader authorizationReader,
        IMountListCandidateReader candidateReader)
    {
        AuthorizationReader = authorizationReader;
        CandidateReader = candidateReader;
    }

    public Task<Result<MountPageDto>> ExecuteAsync(
        ListMountsQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var validation = new ListMountsQueryValidator().Validate(query);
        if (!validation.IsValid)
        {
            return Task.FromResult(validation.ToFailureResult<MountPageDto>());
        }

        throw new NotImplementedException();
    }
}
