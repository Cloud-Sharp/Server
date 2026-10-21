using CloudSharp.Api.Endpoints.Users.Responses;
using CloudSharp.Core.Domain.Users;
using CloudSharp.Core.UseCases.Auth.Dtos;

namespace CloudSharp.Api.Endpoints.Users.Mapping;

internal static class UserResponseMapper
{
    internal static UserProfileResponse ToResponse(UserDto user) =>
        new(
            user.UserPublicId,
            user.Email,
            user.UserName,
            user.DisplayName,
            FormatSystemRole(user.SystemRole),
            FormatStatus(user.Status),
            user.CreatedAt,
            user.UpdatedAt);

    private static string FormatSystemRole(SystemRole role) => role switch
    {
        SystemRole.User => "USER",
        SystemRole.SystemAdmin => "SYSTEM_ADMIN",
        _ => throw new ArgumentOutOfRangeException(nameof(role), role, "Unsupported system role."),
    };

    private static string FormatStatus(UserStatus status) => status switch
    {
        UserStatus.Active => "ACTIVE",
        UserStatus.Suspended => "SUSPENDED",
        UserStatus.Deleted => "DELETED",
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, "Unsupported user status."),
    };
}
