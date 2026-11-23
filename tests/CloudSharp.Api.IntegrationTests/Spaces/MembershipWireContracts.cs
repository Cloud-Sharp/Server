namespace CloudSharp.Api.IntegrationTests.Spaces;

internal sealed record MembershipPageWireResponse(
    IReadOnlyList<MembershipWireResponse> Items,
    string? NextCursor);

internal sealed record MembershipWireResponse(
    Guid MembershipPublicId,
    MembershipUserWireResponse User,
    string Status,
    IReadOnlyList<MembershipRoleWireResponse> Roles,
    DateTimeOffset JoinedAt,
    DateTimeOffset UpdatedAt);

internal sealed record MembershipUserWireResponse(Guid UserPublicId, string? UserName, string? DisplayName);

internal sealed record MembershipRoleWireResponse(Guid RolePublicId, string Code, string Name);

internal sealed record RoleWireResponse(
    Guid RolePublicId,
    string Code,
    string Name,
    bool IsManaged,
    IReadOnlyList<string> Permissions);
