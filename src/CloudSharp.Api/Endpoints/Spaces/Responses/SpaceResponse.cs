namespace CloudSharp.Api.Endpoints.Spaces.Responses;

internal sealed record SpaceResponse(
    Guid SpacePublicId,
    Guid OwnerUserPublicId,
    string Name,
    string? Description,
    string Status,
    SpaceAccessResponse MyAccess,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);
