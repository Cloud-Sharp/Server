namespace CloudSharp.Core.UseCases.Memberships.Dtos;

public sealed record SpaceMembershipPageDto(
    IReadOnlyList<SpaceMembershipDto> Items,
    string? NextCursor);
