using CloudSharp.Core.Domain.Memberships;
using FluentResults;

namespace CloudSharp.Core.Abstractions.Persistence;

/// <summary>루트 DirectoryEntry·Folder와 권한을 포함한 관리형 Role을 같은 UnitOfWork에 등록한다.</summary>
public interface ISpaceBootstrapWriter
{
    Task<Result> AddAsync(
        long spaceId,
        Guid rootFolderPublicId,
        IReadOnlyList<SpaceRole> roles,
        DateTimeOffset createdAt,
        CancellationToken cancellationToken = default);
}
