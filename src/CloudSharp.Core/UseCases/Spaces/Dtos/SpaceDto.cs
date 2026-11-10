using CloudSharp.Core.Domain.Spaces;

namespace CloudSharp.Core.UseCases.Spaces.Dtos;

public sealed record SpaceDto(
    Guid SpacePublicId,
    Guid OwnerUserPublicId,
    string Name,
    string? Description,
    SpaceStatus Status,
    SpaceAccessDto MyAccess,
    Guid RootFolderPublicId,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);
