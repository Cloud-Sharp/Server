namespace CloudSharp.Api.IntegrationTests.Spaces;

internal sealed record SpaceWireResponse(
    Guid SpacePublicId,
    Guid OwnerUserPublicId,
    string Name,
    string? Description,
    string Status,
    SpaceAccessWireResponse MyAccess,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

internal sealed record SpaceAccessWireResponse(
    string Source,
    IReadOnlyList<string> Permissions);

internal sealed record SpacePageWireResponse(
    IReadOnlyList<SpaceWireResponse> Items,
    string? NextCursor);

internal sealed record SpaceQuotaWireResponse(
    Guid SpacePublicId,
    long? AllowedBytes,
    long UsedBytes,
    long ReservedBytes,
    long? AvailableBytes);

internal sealed record SpaceDeletionAcceptedWireResponse(
    Guid SpacePublicId,
    string Status,
    DateTimeOffset AcceptedAt);
