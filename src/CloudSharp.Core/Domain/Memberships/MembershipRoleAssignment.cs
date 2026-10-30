namespace CloudSharp.Core.Domain.Memberships;

/// <summary>
/// Membership에 부여된 Role 하나와 그 부여 기록을 표현하는 불변 값 객체.
/// Role 정의나 Permission은 복사하지 않고 Role 식별자·부여자·부여 시각만 보관한다.
/// </summary>
public sealed record MembershipRoleAssignment
{
    private MembershipRoleAssignment(long roleId, long assignedByUserId, DateTimeOffset assignedAt)
    {
        RoleId = roleId;
        AssignedByUserId = assignedByUserId;
        AssignedAt = assignedAt;
    }

    /// <summary>부여된 Role의 내부 식별자.</summary>
    public long RoleId { get; }

    /// <summary>Role을 부여한 User의 내부 식별자.</summary>
    public long AssignedByUserId { get; }

    /// <summary>부여 시각(UTC).</summary>
    public DateTimeOffset AssignedAt { get; }

    /// <summary>
    /// 유효한 부여 기록을 만든다. 식별자 양수·UTC 시각 불변 조건 위반은 프로그래밍·데이터 오류로 거부한다.
    /// </summary>
    public static MembershipRoleAssignment Create(long roleId, long assignedByUserId, DateTimeOffset assignedAt)
    {
        if (roleId <= 0)
        {
            throw new ArgumentException("Assigned role id must be positive.", nameof(roleId));
        }

        if (assignedByUserId <= 0)
        {
            throw new ArgumentException("Role assigner id must be positive.", nameof(assignedByUserId));
        }

        if (assignedAt.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException("AssignedAt must be UTC.", nameof(assignedAt));
        }

        return new(roleId, assignedByUserId, assignedAt);
    }
}
