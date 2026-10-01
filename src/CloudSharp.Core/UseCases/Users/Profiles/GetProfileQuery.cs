namespace CloudSharp.Core.UseCases.Users.Profiles;

/// <summary>
/// 내 프로필 조회 입력. <see cref="UserId"/>는 endpoint가 검증된 Session context에서만 채우며
/// 클라이언트가 임의로 지정할 수 없다.
/// </summary>
public sealed record GetProfileQuery(long UserId);