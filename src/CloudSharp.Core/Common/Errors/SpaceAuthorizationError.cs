using CloudSharp.Core.Domain.Spaces;

namespace CloudSharp.Core.Common.Errors;

/// <summary>인가 실패의 내부 원인. 외부 HTTP status·오류 표현과 마스킹은 API 경계에서 결정한다.</summary>
public sealed class SpaceAuthorizationError : CloudSharpError
{
    /// <summary>상태 오류일 때 실제 Space 상태. 외부 응답으로 직접 노출하지 않는다.</summary>
    public SpaceStatus? ActualSpaceStatus { get; }

    private SpaceAuthorizationError(string code, string message, SpaceStatus? actualSpaceStatus = null)
        : base(new ErrorDefinition(code), message)
    {
        ActualSpaceStatus = actualSpaceStatus;
    }

    public static SpaceAuthorizationError TokenTypeNotAllowed() =>
        new(ErrorCodes.Auth.TokenTypeNotAllowed, "The actor type is not supported for this operation.");

    public static SpaceAuthorizationError TokenInvalid() =>
        new(ErrorCodes.Auth.TokenInvalid, "The authenticated actor is invalid.");

    public static SpaceAuthorizationError SpaceNotFound() =>
        new(ErrorCodes.Auth.SpaceNotFound, "Space was not found.");

    public static SpaceAuthorizationError AccessDenied() =>
        new(ErrorCodes.Auth.SpaceAccessDenied, "The actor has no access to this space.");

    public static SpaceAuthorizationError StateNotAllowed(SpaceStatus status) =>
        new(ErrorCodes.Auth.SpaceStateNotAllowed, "The space state is not allowed for this operation.", status);

    public static SpaceAuthorizationError PermissionDenied() =>
        new(ErrorCodes.Auth.SpacePermissionDenied, "Required space permissions are missing.");
}
