namespace CloudSharp.Core.Common.Errors;

/// <summary>
/// Space별 Role 생성의 입력·시각 불변 조건에 대한 비즈니스 오류.
/// </summary>
public sealed class SpaceRoleError : CloudSharpError
{
    private SpaceRoleError(string code, string message) : base(new ErrorDefinition(code), message) { }

    public static SpaceRoleError SpaceIdInvalid() =>
        new(ErrorCodes.SpaceRole.SpaceIdInvalid, "Role space id must be positive.");

    public static SpaceRoleError SystemKeyInvalid() =>
        new(ErrorCodes.SpaceRole.SystemKeyInvalid, "Managed role system key must be a defined ADMIN, MEMBER or VIEWER value.");

    public static SpaceRoleError InvalidState(string message) =>
        new(ErrorCodes.SpaceRole.InvalidState, message);
}