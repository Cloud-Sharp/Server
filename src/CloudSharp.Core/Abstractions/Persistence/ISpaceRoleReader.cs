using CloudSharp.Core.Domain.Memberships;
using FluentResults;

namespace CloudSharp.Core.Abstractions.Persistence;

/// <summary>지정한 Space의 Role snapshot을 조회한다.</summary>
public interface ISpaceRoleReader
{
    Task<Result<IReadOnlyList<SpaceRole>>> ReadAsync(
        long spaceId,
        CancellationToken cancellationToken = default);
}
