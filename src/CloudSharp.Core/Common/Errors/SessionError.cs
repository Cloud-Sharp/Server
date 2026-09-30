namespace CloudSharp.Core.Common.Errors;

/// <summary>
/// UserSession 보안 모델 오류. <see cref="CloudSharpError"/>를 상속해
/// <see cref="ErrorCodes.Session"/> 코드를 <see cref="CloudSharpError.ErrorCodeMetadataKey"/> 메타데이터로 노출한다.
/// TokenHash 원문은 메시지나 메타데이터에 포함하지 않는다.
/// </summary>
public sealed class SessionError : CloudSharpError
{
    private SessionError(ErrorDefinition definition, string message) : base(definition, message) { }

    public static SessionError TokenHashInvalid(string message) =>
        new(new ErrorDefinition(ErrorCodes.Session.TokenHashInvalid), message);

    public static SessionError AuthSessionInvalid() =>
        new(new ErrorDefinition(ErrorCodes.Session.AuthSessionInvalid), "Session is invalid or revoked.");

    public static SessionError AuthSessionExpired() =>
        new(new ErrorDefinition(ErrorCodes.Session.AuthSessionExpired), "Session has expired.");

    public static SessionError AuthUserInactive() =>
        new(new ErrorDefinition(ErrorCodes.Session.AuthUserInactive), "User is not active.");

    public static SessionError AuthSessionInvalidState(string message) =>
        new(new ErrorDefinition(ErrorCodes.Session.AuthSessionInvalidState), message);
}