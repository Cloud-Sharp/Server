using System.Collections.Frozen;
using CloudSharp.Core.Common.Errors;
using FluentResults;

namespace CloudSharp.Core.Domain.Memberships;

/// <summary>
/// OWNER를 제외한 User의 Space 참여 상태와 복수 Role 할당을 관리하는 Aggregate.
/// Role 정의·Permission은 저장하지 않고 저장된 Role snapshot의 식별자와 부여 기록만 복사한다.
/// OWNER와 동일한 User의 Membership 생성 차단, 계정·Space 상태 확인, 활성 Membership 유일성,
/// 요청자 권한, 초대 가입 Role 제한과 동시성은 UseCase·저장소가 검증한다.
/// </summary>
public sealed class SpaceMembership
{
    /// <summary>내부 PK. 신규 객체는 0이며 외부 응답에 노출하지 않는다.</summary>
    public long Id { get; private set; }

    /// <summary>생성 후 변경하지 않는 UUIDv7 외부 식별자. 멤버 관리 API에서 사용한다.</summary>
    public Guid PublicId { get; private set; }

    /// <summary>참여 대상 Space의 내부 FK.</summary>
    public long SpaceId { get; private set; }

    /// <summary>참여 User의 내부 FK.</summary>
    public long UserId { get; private set; }

    /// <summary>참여 수명주기 상태. LEFT는 terminal이다.</summary>
    public SpaceMembershipStatus Status { get; private set; }

    public long Version { get; private set; }
    public DateTimeOffset JoinedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>SUSPENDED에서만 존재하는 정지 시각.</summary>
    public DateTimeOffset? SuspendedAt { get; private set; }

    /// <summary>LEFT에서만 존재하는 탈퇴·제거 시각.</summary>
    public DateTimeOffset? LeftAt { get; private set; }

    /// <summary>
    /// 현재 부여된 Role 할당의 불변 snapshot. RoleId 기준 유일하며, 실제 교체마다 새 snapshot으로 갈아끼운다.
    /// LEFT 상태에서도 기록을 유지한다.
    /// </summary>
    public IReadOnlySet<MembershipRoleAssignment> RoleAssignments { get; private set; } =
        FrozenSet<MembershipRoleAssignment>.Empty;

    private SpaceMembership() { }

    /// <summary>
    /// 하나 이상의 Role을 부여한 새 ACTIVE Membership을 만든다. Role은 저장된 양수 ID를 가진 동일 Space의
    /// 비삭제 snapshot이어야 한다. Space OWNER 생성 차단과 계정·Space 상태, 활성 Membership 유일성,
    /// 초대 가입 Role 제한은 호출 UseCase가 검증한다.
    /// </summary>
    public static Result<SpaceMembership> Create(
        long spaceId,
        long userId,
        IReadOnlyCollection<SpaceRole> roles,
        long assignedByUserId,
        DateTimeOffset now)
    {
        if (spaceId <= 0)
        {
            return Result.Fail<SpaceMembership>(SpaceMembershipError.SpaceIdInvalid());
        }

        if (userId <= 0)
        {
            return Result.Fail<SpaceMembership>(SpaceMembershipError.UserIdInvalid());
        }

        if (now.Offset != TimeSpan.Zero)
        {
            return Result.Fail<SpaceMembership>(SpaceMembershipError.InvalidState("Create requires UTC now."));
        }

        if (assignedByUserId <= 0)
        {
            return Result.Fail<SpaceMembership>(SpaceMembershipError.AssignedByInvalid());
        }

        var rolesValidation = ValidateRoleSnapshots(roles, spaceId);
        if (rolesValidation.IsFailed)
        {
            return Result.Fail<SpaceMembership>(rolesValidation.Errors);
        }

        return Result.Ok(new SpaceMembership
        {
            PublicId = Guid.CreateVersion7(),
            SpaceId = spaceId,
            UserId = userId,
            Status = SpaceMembershipStatus.Active,
            Version = 1,
            JoinedAt = now,
            UpdatedAt = now,
            RoleAssignments = BuildAssignments(roles, assignedByUserId, now),
        });
    }

    /// <summary>
    /// 영속 snapshot과 Role 할당 집합을 손실 없이 복원한다. 잘못된 영속 상태는 프로그래밍·데이터 오류로 거부하며
    /// 복원 중 Version·시각·부여 기록을 변경하지 않는다. ACTIVE 복원은 하나 이상의 Role 할당을 요구하고
    /// SUSPENDED·LEFT의 빈 할당은 허용한다.
    /// </summary>
    public static SpaceMembership Reconstitute(
        long id,
        Guid publicId,
        long spaceId,
        long userId,
        SpaceMembershipStatus status,
        long version,
        DateTimeOffset joinedAt,
        DateTimeOffset updatedAt,
        DateTimeOffset? suspendedAt,
        DateTimeOffset? leftAt,
        IEnumerable<MembershipRoleAssignment> roleAssignments)
    {
        if (id < 0)
        {
            throw new ArgumentException("Membership id must not be negative.", nameof(id));
        }

        if (publicId == Guid.Empty)
        {
            throw new ArgumentException("Membership public id must not be empty.", nameof(publicId));
        }

        if (spaceId <= 0)
        {
            throw new ArgumentException("Membership space id must be positive.", nameof(spaceId));
        }

        if (userId <= 0)
        {
            throw new ArgumentException("Membership user id must be positive.", nameof(userId));
        }

        if (!Enum.IsDefined(status))
        {
            throw new ArgumentOutOfRangeException(nameof(status), status, "Unsupported membership status.");
        }

        if (version < 1)
        {
            throw new ArgumentException("Version must be positive.", nameof(version));
        }

        if (joinedAt.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException("JoinedAt must be UTC.", nameof(joinedAt));
        }

        ValidateSnapshotTimestamp(updatedAt, nameof(updatedAt), joinedAt, updatedAt);
        ValidateSnapshotTimestamp(suspendedAt, nameof(suspendedAt), joinedAt, updatedAt);
        ValidateSnapshotTimestamp(leftAt, nameof(leftAt), joinedAt, updatedAt);

        if ((status == SpaceMembershipStatus.Suspended) != suspendedAt.HasValue)
        {
            throw new ArgumentException(
                "SuspendedAt must be present only for SUSPENDED status.", nameof(suspendedAt));
        }

        if ((status == SpaceMembershipStatus.Left) != leftAt.HasValue)
        {
            throw new ArgumentException("LeftAt must be present only for LEFT status.", nameof(leftAt));
        }

        ArgumentNullException.ThrowIfNull(roleAssignments);

        var storedRoleIds = new HashSet<long>();
        var storedAssignments = new List<MembershipRoleAssignment>();
        foreach (var assignment in roleAssignments)
        {
            if (assignment is null)
            {
                throw new ArgumentException("Role assignment must not be null.", nameof(roleAssignments));
            }

            if (assignment.AssignedAt < joinedAt || assignment.AssignedAt > updatedAt)
            {
                throw new ArgumentException(
                    "AssignedAt must be between JoinedAt and UpdatedAt.", nameof(roleAssignments));
            }

            if (!storedRoleIds.Add(assignment.RoleId))
            {
                throw new ArgumentException(
                    "Duplicate role assignment in snapshot.", nameof(roleAssignments));
            }

            storedAssignments.Add(assignment);
        }

        if (status == SpaceMembershipStatus.Active && storedAssignments.Count == 0)
        {
            throw new ArgumentException(
                "An ACTIVE membership requires at least one role assignment.", nameof(roleAssignments));
        }

        return new SpaceMembership
        {
            Id = id,
            PublicId = publicId,
            SpaceId = spaceId,
            UserId = userId,
            Status = status,
            Version = version,
            JoinedAt = joinedAt,
            UpdatedAt = updatedAt,
            SuspendedAt = suspendedAt,
            LeftAt = leftAt,
            RoleAssignments = storedAssignments.ToFrozenSet(),
        };
    }

    /// <summary>
    /// Role 집합을 전체 교체한다. ACTIVE·SUSPENDED에서 허용한다. 유지되는 Role의 부여 기록은 보존하고
    /// 새 Role에만 현재 부여자·시각을 기록한다. 같은 Role ID 집합은 순서·부여자와 관계없이 변경 없는 성공이다.
    /// 요청자 권한과 초대 Role 제한은 UseCase가 검증한다.
    /// </summary>
    public Result ReplaceRoles(IReadOnlyCollection<SpaceRole> roles, long assignedByUserId, DateTimeOffset now)
    {
        var validation = EnsureActiveOrSuspended(now);
        if (validation.IsFailed)
        {
            return validation;
        }

        if (assignedByUserId <= 0)
        {
            return Result.Fail(SpaceMembershipError.AssignedByInvalid());
        }

        validation = ValidateRoleSnapshots(roles, SpaceId);
        if (validation.IsFailed)
        {
            return validation;
        }

        var newRoleIds = roles.Select(role => role.Id).ToHashSet();
        var currentRoleIds = RoleAssignments.Select(assignment => assignment.RoleId).ToHashSet();
        if (currentRoleIds.SetEquals(newRoleIds))
        {
            return Result.Ok();
        }

        var replacement = RoleAssignments
            .Where(assignment => newRoleIds.Contains(assignment.RoleId))
            .Concat(roles
                .Where(role => !currentRoleIds.Contains(role.Id))
                .Select(role => MembershipRoleAssignment.Create(role.Id, assignedByUserId, now)))
            .ToFrozenSet();

        return ApplyChange(now, () => RoleAssignments = replacement);
    }

    /// <summary>ACTIVE 멤버를 정지해 권한 평가에서 제외한다.</summary>
    public Result Suspend(DateTimeOffset now)
    {
        if (Status == SpaceMembershipStatus.Left)
        {
            return Result.Fail(SpaceMembershipError.Inactive());
        }

        if (Status != SpaceMembershipStatus.Active)
        {
            return Result.Fail(SpaceMembershipError.InvalidState("Only ACTIVE memberships can be suspended."));
        }

        var validation = ValidateNow(now);
        if (validation.IsFailed)
        {
            return validation;
        }

        return ApplyChange(now, () =>
        {
            Status = SpaceMembershipStatus.Suspended;
            SuspendedAt = now;
        });
    }

    /// <summary>정지된 멤버를 활성화한다. Role 할당이 하나 이상 남아 있어야 한다.</summary>
    public Result Activate(DateTimeOffset now)
    {
        if (Status == SpaceMembershipStatus.Left)
        {
            return Result.Fail(SpaceMembershipError.Inactive());
        }

        if (Status != SpaceMembershipStatus.Suspended)
        {
            return Result.Fail(SpaceMembershipError.InvalidState("Only SUSPENDED memberships can be activated."));
        }

        var validation = ValidateNow(now);
        if (validation.IsFailed)
        {
            return validation;
        }

        if (RoleAssignments.Count == 0)
        {
            return Result.Fail(SpaceMembershipError.RolesRequired());
        }

        return ApplyChange(now, () =>
        {
            Status = SpaceMembershipStatus.Active;
            SuspendedAt = null;
        });
    }

    /// <summary>멤버 스스로 Space를 떠난다. 반복 호출은 비활성 오류이며 탈퇴 API의 멱등성은 UseCase가 담당한다.</summary>
    public Result Leave(DateTimeOffset now) => LeaveSpace(now);

    /// <summary>관리자가 멤버를 제거한다. 전이와 기록 정리는 <see cref="Leave"/>와 동일하다.</summary>
    public Result Remove(DateTimeOffset now) => LeaveSpace(now);

    private Result LeaveSpace(DateTimeOffset now)
    {
        if (Status == SpaceMembershipStatus.Left)
        {
            return Result.Fail(SpaceMembershipError.Inactive());
        }

        var validation = ValidateNow(now);
        if (validation.IsFailed)
        {
            return validation;
        }

        return ApplyChange(now, () =>
        {
            Status = SpaceMembershipStatus.Left;
            SuspendedAt = null;
            LeftAt = now;
        });
    }

    private Result EnsureActiveOrSuspended(DateTimeOffset now) =>
        Status == SpaceMembershipStatus.Left
            ? Result.Fail(SpaceMembershipError.Inactive())
            : ValidateNow(now);

    private Result ValidateNow(DateTimeOffset now)
    {
        if (now.Offset != TimeSpan.Zero || now < UpdatedAt)
        {
            return Result.Fail(
                SpaceMembershipError.InvalidState("Operation requires UTC now at or after UpdatedAt."));
        }

        return Result.Ok();
    }

    private Result ApplyChange(DateTimeOffset now, Action change)
    {
        if (Version == long.MaxValue)
        {
            return Result.Fail(
                SpaceMembershipError.InvalidState("Membership version cannot be advanced further."));
        }

        change();
        Version++;
        UpdatedAt = now;
        return Result.Ok();
    }

    private static Result ValidateRoleSnapshots(IReadOnlyCollection<SpaceRole> roles, long spaceId)
    {
        if (roles is null || roles.Count == 0)
        {
            return Result.Fail(SpaceMembershipError.RolesRequired());
        }

        var seenRoleIds = new HashSet<long>();
        foreach (var role in roles)
        {
            if (role is null)
            {
                return Result.Fail(SpaceMembershipError.RoleInvalid("Role must not be null."));
            }

            if (role.Id <= 0)
            {
                return Result.Fail(SpaceMembershipError.RoleInvalid("Only a persisted role can be assigned."));
            }

            if (role.SpaceId != spaceId)
            {
                return Result.Fail(SpaceMembershipError.RoleInvalid("Role belongs to a different space."));
            }

            if (role.DeletedAt is not null)
            {
                return Result.Fail(SpaceMembershipError.RoleInvalid("A deleted role cannot be assigned."));
            }

            if (!seenRoleIds.Add(role.Id))
            {
                return Result.Fail(SpaceMembershipError.RoleInvalid("Duplicate role assignment is not allowed."));
            }
        }

        return Result.Ok();
    }

    private static FrozenSet<MembershipRoleAssignment> BuildAssignments(
        IEnumerable<SpaceRole> roles,
        long assignedByUserId,
        DateTimeOffset assignedAt) =>
        roles.Select(role => MembershipRoleAssignment.Create(role.Id, assignedByUserId, assignedAt))
            .ToFrozenSet();

    private static void ValidateSnapshotTimestamp(
        DateTimeOffset? value,
        string parameterName,
        DateTimeOffset joinedAt,
        DateTimeOffset updatedAt)
    {
        if (value is DateTimeOffset timestamp
            && (timestamp.Offset != TimeSpan.Zero || timestamp < joinedAt || timestamp > updatedAt))
        {
            throw new ArgumentException("Timestamp must be UTC and between JoinedAt and UpdatedAt.", parameterName);
        }
    }
}
