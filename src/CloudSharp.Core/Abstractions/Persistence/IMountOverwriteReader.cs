using CloudSharp.Core.Domain.Mounts;
using FluentResults;

namespace CloudSharp.Core.Abstractions.Persistence;

/// <summary>Mount의 Role 또는 사용자 권한 덮어쓰기를 대상 PublicId 오름차순 페이지로 조회한다.</summary>
public interface IMountOverwriteReader
{
    Task<Result<MountOverwritePage>> ReadAsync(
        long spaceId,
        long mountId,
        MountOverwriteTargetType targetType,
        string? cursor,
        int limit,
        CancellationToken cancellationToken = default);
}
