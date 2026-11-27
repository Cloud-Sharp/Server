using CloudSharp.Core.Abstractions.Persistence;
using CloudSharp.Core.Common.Errors;
using CloudSharp.Core.Common.Results;
using CloudSharp.Core.UseCases.Mounts.Dtos;
using FluentResults;

namespace CloudSharp.Core.UseCases.Mounts.Details;

public sealed class GetMountUseCase : IUseCase<GetMountQuery, MountResultDto>
{
    private IMountReader MountReader { get; } = null!;

    public GetMountUseCase() { }

    public GetMountUseCase(IMountReader mountReader)
    {
        MountReader = mountReader;
    }

    public async Task<Result<MountResultDto>> ExecuteAsync(
        GetMountQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var validation = new GetMountQueryValidator().Validate(query);
        if (!validation.IsValid)
        {
            return validation.ToFailureResult<MountResultDto>();
        }

        if (MountReader is null)
        {
            return Result.Fail<MountResultDto>(CommonError.DependencyUnavailable());
        }

        var context = query.Mount;
        var mountResult = await MountReader.ReadAsync(
            context.Space.SpaceId,
            context.MountPublicId,
            cancellationToken);
        if (mountResult.IsFailed)
        {
            return Result.Fail<MountResultDto>(mountResult.Errors);
        }

        var mount = mountResult.Value;
        if (mount is null || mount.Id != context.MountId || mount.SpaceId != context.Space.SpaceId)
        {
            return Result.Fail<MountResultDto>(CommonError.ResourceNotFound());
        }

        var dto = new MountDto(
            context.Space.SpacePublicId,
            mount.PublicId,
            mount.Name,
            mount.Visibility,
            mount.Status,
            MountAccessDto.From(context.EffectivePermissions),
            mount.CreatedAt,
            mount.UpdatedAt,
            mount.ArchivedAt);
        return Result.Ok(new MountResultDto(dto, mount.Version));
    }
}
