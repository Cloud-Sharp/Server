using CloudSharp.Core.Abstractions.Persistence;
using CloudSharp.Core.Common.Results;
using CloudSharp.Core.UseCases.Spaces.Dtos;
using FluentResults;

namespace CloudSharp.Core.UseCases.Spaces.Listings;

public sealed class ListSpacesUseCase : IUseCase<ListSpacesQuery, SpacePageDto>
{
    private ISpaceListCandidateReader CandidateReader { get; } = null!;

    public ListSpacesUseCase() { }

    internal ListSpacesUseCase(ISpaceListCandidateReader candidateReader) => CandidateReader = candidateReader;

    public Task<Result<SpacePageDto>> ExecuteAsync(
        ListSpacesQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var validation = new ListSpacesQueryValidator().Validate(query);
        if (!validation.IsValid)
        {
            return Task.FromResult(validation.ToFailureResult<SpacePageDto>());
        }

        throw new NotImplementedException();
    }
}
