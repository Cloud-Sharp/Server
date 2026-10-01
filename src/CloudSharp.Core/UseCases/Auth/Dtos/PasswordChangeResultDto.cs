namespace CloudSharp.Core.UseCases.Auth.Dtos;

/// <summary>
/// 비밀번호 변경 결과. 변경된 User <see cref="Version"/>만 반환하며
/// endpoint는 이 값을 성공 응답의 ETag로 사용한다. 비밀번호는 반환하지 않는다.
/// </summary>
public sealed record PasswordChangeResultDto(long Version);