namespace CloudSharp.Core.UseCases.Memberships.Dtos;

public sealed record SpaceMembershipUserDto(
    Guid UserPublicId,
    string? UserName,
    string? DisplayName);
