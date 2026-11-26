using CloudSharp.Core.Domain.Mounts;

namespace CloudSharp.Core.UseCases.Mounts.Dtos;

/// <summary>요청자가 행사할 수 있는 파일 권한과 현재 설정 변경 자격이다.</summary>
public sealed record MountAccessDto(IReadOnlySet<MountPermission> Permissions, bool CanManage);
