using CloudSharp.Core.UseCases.Auth.Dtos;

namespace CloudSharp.Core.UseCases.Auth.Authentication;

/// <summary>
/// 인증된 Session actor. token 원문·hash는 포함하지 않으며 최신 User 정보를 담는다.
/// </summary>
public sealed record AuthenticatedSessionResult(long UserId, UserDto User, Guid SessionId);
