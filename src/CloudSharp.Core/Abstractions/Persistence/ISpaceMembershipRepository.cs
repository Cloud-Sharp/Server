using CloudSharp.Core.Domain.Memberships;
using FluentResults;

namespace CloudSharp.Core.Abstractions.Persistence;

/// <summary>조회는 독립 snapshot을 반환하고 Update는 Unit of Work가 저장할 변경을 등록한다.</summary>
public interface ISpaceMembershipRepository
{
    Task<Result<SpaceMembership?>> FindByPublicIdAsync(
        long spaceId,
        Guid membershipPublicId,
        CancellationToken cancellationToken = default);

    /// <summary>ACTIVE 또는 SUSPENDED Membership을 우선하고 없으면 가장 최근 LEFT 기록을 조회한다.</summary>
    Task<Result<SpaceMembership?>> FindLatestByUserIdAsync(
        long spaceId,
        long userId,
        CancellationToken cancellationToken = default);

    Task<Result> AddAsync(
        SpaceMembership membership,
        CancellationToken cancellationToken = default);

    /// <summary>조회 시 관찰한 버전을 expectedVersion으로 전달한다.</summary>
    Task<Result> UpdateAsync(
        SpaceMembership membership,
        long expectedVersion,
        CancellationToken cancellationToken = default);
}
