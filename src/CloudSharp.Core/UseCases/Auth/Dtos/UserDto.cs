using CloudSharp.Core.Domain.Users;

namespace CloudSharp.Core.UseCases.Auth.Dtos;

/// <summary>
/// 외부 식별자 기준 사용자 정보. 내부 Id, password hash, token hash, SecurityVersion은 포함하지 않는다.
/// </summary>
public sealed record UserDto(
    Guid UserPublicId,
    string Email,
    string? UserName,
    string? DisplayName,
    SystemRole SystemRole,
    UserStatus Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);