namespace CloudSharp.Core.Common.Errors;

/// <summary>
/// User 도메인 오류. <see cref="CloudSharpError"/>를 상속해
/// <see cref="ErrorCodes.User"/> 코드를 <see cref="CloudSharpError.ErrorCodeMetadataKey"/> 메타데이터로 노출한다.
/// 비밀번호 해시, token hash 등 민감값은 메시지나 메타데이터에 포함하지 않는다.
/// </summary>
public sealed class UserError : CloudSharpError
{
    private UserError(ErrorDefinition definition, string message) : base(definition, message) { }

    public static UserError InvalidEmail(string message) =>
        new(new ErrorDefinition(ErrorCodes.User.InvalidEmail), message);

    public static UserError NameInvalid(string message) =>
        new(new ErrorDefinition(ErrorCodes.User.NameInvalid), message);

    public static UserError DisplayNameInvalid(string message) =>
        new(new ErrorDefinition(ErrorCodes.User.DisplayNameInvalid), message);

    public static UserError PasswordInvalid(string message) =>
        new(new ErrorDefinition(ErrorCodes.User.PasswordInvalid), message);

    public static UserError InvalidRole(string message) =>
        new(new ErrorDefinition(ErrorCodes.User.InvalidRole), message);

    public static UserError InvalidStatus(string message) =>
        new(new ErrorDefinition(ErrorCodes.User.InvalidStatus), message);

    public static UserError Deleted() =>
        new(new ErrorDefinition(ErrorCodes.User.Deleted), "User is deleted and cannot be modified.");

    public static UserError InvalidState(string message) =>
        new(new ErrorDefinition(ErrorCodes.User.InvalidState), message);
}