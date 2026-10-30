namespace CloudSharp.Core.Common.Errors;

/// <summary>
/// Space Membership의 생성·Role 할당·상태 전이 불변 조건에 대한 비즈니스 오류.
/// </summary>
public sealed class SpaceMembershipError : CloudSharpError
{
    private SpaceMembershipError(string code, string message) : base(new ErrorDefinition(code), message) { }

    public static SpaceMembershipError SpaceIdInvalid() =>
        new(ErrorCodes.SpaceMembership.SpaceIdInvalid, "Membership space id must be positive.");

    public static SpaceMembershipError UserIdInvalid() =>
        new(ErrorCodes.SpaceMembership.UserIdInvalid, "Membership user id must be positive.");

    public static SpaceMembershipError AssignedByInvalid() =>
        new(ErrorCodes.SpaceMembership.AssignedByInvalid, "Role assigner user id must be positive.");

    public static SpaceMembershipError RolesRequired() =>
        new(ErrorCodes.SpaceMembership.RoleInvalid, "Membership requires at least one role.");

    public static SpaceMembershipError RoleInvalid(string message) =>
        new(ErrorCodes.SpaceMembership.RoleInvalid, message);

    public static SpaceMembershipError Inactive() =>
        new(ErrorCodes.SpaceMembership.Inactive, "Membership has left the space and cannot be changed.");

    public static SpaceMembershipError InvalidState(string message) =>
        new(ErrorCodes.SpaceMembership.InvalidState, message);
}
