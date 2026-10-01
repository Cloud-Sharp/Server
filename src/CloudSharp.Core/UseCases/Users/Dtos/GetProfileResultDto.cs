using CloudSharp.Core.UseCases.Auth.Dtos;

namespace CloudSharp.Core.UseCases.Users.Dtos;

/// <summary>
/// 내 프로필 조회 결과. <see cref="User"/>를 리소스 본문으로 반환하고
/// <see cref="Version"/>은 endpoint가 ETag <c>"v{Version}"</c>으로 변환한다.
/// 내부 Id, password hash, SecurityVersion은 포함하지 않는다.
/// </summary>
public sealed record GetProfileResultDto(UserDto User, long Version);