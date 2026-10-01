namespace CloudSharp.Core.Common.Errors;

/// <summary>
/// 도메인 오류 코드 문자열 상수. <see cref="CloudSharpError"/> 메타데이터와
/// API 오류 매핑 카탈로그가 공유하는 단일 출처다. 새 코드는 반드시 여기에 추가한다.
/// </summary>
public static class ErrorCodes
{
    public static class Common
    {
        public const string DependencyUnavailable = "DEPENDENCY_UNAVAILABLE";
    }

    public static class User
    {
        public const string InvalidEmail = "USER_INVALID_EMAIL";
        public const string NameInvalid = "USER_NAME_INVALID";
        public const string DisplayNameInvalid = "USER_DISPLAY_NAME_INVALID";
        public const string PasswordInvalid = "USER_PASSWORD_INVALID";
        public const string InvalidRole = "USER_INVALID_ROLE";
        public const string InvalidStatus = "USER_INVALID_STATUS";
        public const string Deleted = "USER_DELETED";
        public const string InvalidState = "USER_INVALID_STATE";
        public const string EmailConflict = "USER_EMAIL_CONFLICT";
        public const string NameConflict = "USER_NAME_CONFLICT";
    }

    public static class Session
    {
        public const string TokenHashInvalid = "TOKEN_HASH_INVALID";
        public const string AuthSessionInvalid = "AUTH_SESSION_INVALID";
        public const string AuthSessionExpired = "AUTH_SESSION_EXPIRED";
        public const string AuthUserInactive = "AUTH_USER_INACTIVE";
        public const string AuthSessionInvalidState = "AUTH_SESSION_INVALID_STATE";
    }
}