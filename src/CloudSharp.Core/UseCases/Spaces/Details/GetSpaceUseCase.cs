using CloudSharp.Core.Abstractions.Persistence;
using CloudSharp.Core.Common.Errors;
using CloudSharp.Core.Common.Results;
using CloudSharp.Core.Domain.Spaces;
using CloudSharp.Core.Policies.Authorization;
using CloudSharp.Core.UseCases.Spaces.Dtos;
using FluentResults;

namespace CloudSharp.Core.UseCases.Spaces.Details;

public sealed class GetSpaceUseCase : IUseCase<GetSpaceQuery, SpaceResultDto>
{
    private ISpaceDetailsReader DetailsReader { get; } = null!;

    public GetSpaceUseCase() { }

    public GetSpaceUseCase(ISpaceDetailsReader detailsReader) => DetailsReader = detailsReader;

    public async Task<Result<SpaceResultDto>> ExecuteAsync(
        GetSpaceQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var validation = new GetSpaceQueryValidator().Validate(query);
        if (!validation.IsValid)
        {
            return validation.ToFailureResult<SpaceResultDto>();
        }

        if (DetailsReader is null)
        {
            return Result.Fail<SpaceResultDto>(CommonError.DependencyUnavailable());
        }

        var readResult = await DetailsReader.ReadAsync(query.Space.SpaceId, cancellationToken);
        if (readResult.IsFailed)
        {
            return Result.Fail<SpaceResultDto>(readResult.Errors);
        }

        var snapshot = readResult.Value;
        if (snapshot is null)
        {
            return Result.Fail<SpaceResultDto>(SpaceAuthorizationError.SpaceNotFound());
        }

        var space = snapshot.Space;
        if (space.Id != query.Space.SpaceId || space.PublicId != query.Space.SpacePublicId)
        {
            throw new InvalidOperationException("Space details reader returned a different space.");
        }

        if (space.Status is not (SpaceStatus.Active or SpaceStatus.Archived))
        {
            return Result.Fail<SpaceResultDto>(SpaceAuthorizationError.StateNotAllowed(space.Status));
        }

        var dto = new SpaceDto(
            space.PublicId,
            snapshot.OwnerUserPublicId,
            space.Name,
            space.Description,
            space.Status,
            new SpaceAccessDto(query.Space.AuthorizationSource, query.Space.EffectivePermissions),
            space.CreatedAt,
            space.UpdatedAt);
        return Result.Ok(new SpaceResultDto(dto, space.Version));
    }
}
