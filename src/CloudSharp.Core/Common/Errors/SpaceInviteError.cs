namespace CloudSharp.Core.Common.Errors;

/// <summary>
/// Space Invite의 수명주기·수락 한도 불변 조건에 대한 비즈니스 오류.
/// </summary>
public sealed class SpaceInviteError : CloudSharpError
{
    private SpaceInviteError(string code, string message) : base(new ErrorDefinition(code), message) { }

    public static SpaceInviteError InvalidState(string message) =>
        new(ErrorCodes.SpaceInvite.InvalidState, message);

    public static SpaceInviteError Revoked() =>
        new(ErrorCodes.SpaceInvite.Revoked, "Invite has been revoked.");

    public static SpaceInviteError Expired() =>
        new(ErrorCodes.SpaceInvite.Expired, "Invite has expired.");

    public static SpaceInviteError UsageExhausted() =>
        new(ErrorCodes.SpaceInvite.UsageExhausted, "Invite usage limit has been reached.");
}