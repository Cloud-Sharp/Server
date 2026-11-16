namespace CloudSharp.Core.UseCases.Invites.Dtos;

public sealed record SpaceInvitePageDto(
    IReadOnlyList<SpaceInviteDto> Items,
    string? NextCursor);
