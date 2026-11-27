using CloudSharp.Core.Abstractions.Persistence;
using CloudSharp.Core.Common.Errors;
using CloudSharp.Core.Common.Results;
using CloudSharp.Core.UseCases.Mounts.Dtos;
using FluentResults;

namespace CloudSharp.Core.UseCases.Mounts.PermissionListings;

public sealed class ListMountPermissionsUseCase : IUseCase<ListMountPermissionsQuery, MountPermissionPageDto>
{
    private IMountOverwriteReader OverwriteReader { get; } = null!;

    public ListMountPermissionsUseCase() { }

    public ListMountPermissionsUseCase(IMountOverwriteReader overwriteReader)
    {
        OverwriteReader = overwriteReader;
    }

    public async Task<Result<MountPermissionPageDto>> ExecuteAsync(
        ListMountPermissionsQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var validation = new ListMountPermissionsQueryValidator().Validate(query);
        if (!validation.IsValid)
        {
            return validation.ToFailureResult<MountPermissionPageDto>();
        }

        if (OverwriteReader is null)
        {
            return Result.Fail<MountPermissionPageDto>(CommonError.DependencyUnavailable());
        }

        var readResult = await OverwriteReader.ReadAsync(
            query.Mount.Space.SpaceId,
            query.Mount.MountId,
            query.TargetType,
            query.Cursor,
            query.Limit,
            cancellationToken);
        if (readResult.IsFailed)
        {
            return Result.Fail<MountPermissionPageDto>(readResult.Errors);
        }

        var page = readResult.Value;
        var items = page.Overwrites.Select(snapshot => new MountPermissionOverwriteDto(
            snapshot.Overwrite.TargetType,
            snapshot.TargetPublicId,
            snapshot.Overwrite.Allow,
            snapshot.Overwrite.Deny)).ToArray();
        return Result.Ok(new MountPermissionPageDto(items, page.NextCursor));
    }
}
