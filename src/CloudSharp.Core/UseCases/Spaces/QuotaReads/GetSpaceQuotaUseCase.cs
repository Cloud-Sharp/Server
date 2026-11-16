using CloudSharp.Core.Abstractions.Persistence;
using CloudSharp.Core.Common.Errors;
using CloudSharp.Core.Common.Results;
using CloudSharp.Core.Domain.Spaces;
using CloudSharp.Core.UseCases.Spaces.Dtos;
using FluentResults;

namespace CloudSharp.Core.UseCases.Spaces.QuotaReads;

public sealed class GetSpaceQuotaUseCase : IUseCase<GetSpaceQuotaQuery, SpaceQuotaDto>
{
    private ISpaceRepository SpaceRepository { get; } = null!;

    public GetSpaceQuotaUseCase() { }

    internal GetSpaceQuotaUseCase(ISpaceRepository spaceRepository) => SpaceRepository = spaceRepository;

    public async Task<Result<SpaceQuotaDto>> ExecuteAsync(
        GetSpaceQuotaQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var validation = new GetSpaceQuotaQueryValidator().Validate(query);
        if (!validation.IsValid)
        {
            return validation.ToFailureResult<SpaceQuotaDto>();
        }

        if (SpaceRepository is null)
        {
            return Result.Fail<SpaceQuotaDto>(CommonError.DependencyUnavailable());
        }

        var findResult = await SpaceRepository.FindByIdAsync(query.Space.SpaceId, cancellationToken);
        if (findResult.IsFailed)
        {
            return Result.Fail<SpaceQuotaDto>(findResult.Errors);
        }

        var space = findResult.Value;
        if (space is null)
        {
            return Result.Fail<SpaceQuotaDto>(SpaceAuthorizationError.SpaceNotFound());
        }

        if (space.Id != query.Space.SpaceId || space.PublicId != query.Space.SpacePublicId)
        {
            throw new InvalidOperationException("Space repository returned a different space.");
        }

        if (space.Status is not (SpaceStatus.Active or SpaceStatus.Archived))
        {
            return Result.Fail<SpaceQuotaDto>(SpaceAuthorizationError.StateNotAllowed(space.Status));
        }

        return Result.Ok(SpaceQuotaDto.FromSpace(space));
    }
}
