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
        public const string PreconditionFailed = "PRECONDITION_FAILED";
        public const string PreconditionRequired = "PRECONDITION_REQUIRED";
    }

    public static class Auth
    {
        public const string TokenRequired = "AUTH_TOKEN_REQUIRED";
        public const string TokenInvalid = "AUTH_TOKEN_INVALID";
        public const string TokenTypeNotAllowed = "AUTH_TOKEN_TYPE_NOT_ALLOWED";
    }

    public static class User
    {
        public const string InvalidEmail = "USER_INVALID_EMAIL";
        public const string NameInvalid = "USER_NAME_INVALID";
        public const string DisplayNameInvalid = "USER_DISPLAY_NAME_INVALID";
        public const string PasswordInvalid = "USER_PASSWORD_INVALID";
        public const string PasswordMismatch = "USER_PASSWORD_MISMATCH";
        public const string InvalidRole = "USER_INVALID_ROLE";
        public const string InvalidStatus = "USER_INVALID_STATUS";
        public const string Deleted = "USER_DELETED";
        public const string InvalidState = "USER_INVALID_STATE";
        public const string EmailConflict = "USER_EMAIL_CONFLICT";
        public const string NameConflict = "USER_NAME_CONFLICT";
    }

    public static class Space
    {
        public const string NameInvalid = "SPACE_NAME_INVALID";
        public const string OwnerInvalid = "SPACE_OWNER_INVALID";
        public const string Inactive = "SPACE_INACTIVE";
        public const string InvalidState = "SPACE_INVALID_STATE";
        public const string QuotaInvalid = "SPACE_QUOTA_INVALID";
        public const string QuotaExceeded = "SPACE_QUOTA_EXCEEDED";
        public const string QuotaTooSmall = "SPACE_QUOTA_TOO_SMALL";
        public const string StorageAmountInvalid = "SPACE_STORAGE_AMOUNT_INVALID";
        public const string ReservationInvalid = "SPACE_RESERVATION_INVALID";
        public const string TransferTargetInvalid = "SPACE_TRANSFER_TARGET_INVALID";
    }

    public static class SpaceRole
    {
        public const string SpaceIdInvalid = "SPACE_ROLE_SPACE_ID_INVALID";
        public const string SystemKeyInvalid = "SPACE_ROLE_SYSTEM_KEY_INVALID";
        public const string InvalidState = "SPACE_ROLE_INVALID_STATE";
    }

    public static class SpaceMembership
    {
        public const string SpaceIdInvalid = "MEMBER_SPACE_ID_INVALID";
        public const string UserIdInvalid = "MEMBER_USER_ID_INVALID";
        public const string AssignedByInvalid = "MEMBER_ASSIGNED_BY_INVALID";
        public const string RoleInvalid = "MEMBER_INVALID_ROLE";
        public const string InvalidState = "MEMBER_INVALID_STATE";
        public const string Inactive = "MEMBER_INACTIVE";
    }

    public static class Session
    {
        public const string TokenHashInvalid = "TOKEN_HASH_INVALID";
        public const string AuthInvalidCredentials = "AUTH_INVALID_CREDENTIALS";
        public const string AuthSessionInvalid = "AUTH_SESSION_INVALID";
        public const string AuthSessionExpired = "AUTH_SESSION_EXPIRED";
        public const string AuthUserInactive = "AUTH_USER_INACTIVE";
        public const string AuthSessionInvalidState = "AUTH_SESSION_INVALID_STATE";
    }
}
