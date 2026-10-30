using CloudSharp.Core.Common.Errors;
using CloudSharp.Core.Common.Results;
using CloudSharp.Core.Common.Tokens;
using CloudSharp.Core.Domain.Memberships;
using FluentResults;
using FluentValidation;

namespace CloudSharp.Core.Domain.Invites;

/// <summary>
/// 만료 전까지 여러 사용자가 재사용할 수 있는 Space 참여 초대 링크를 관리하는 Aggregate.
/// 초대 token은 hash로만 보관하고 원문·URL을 저장하지 않는다. 신규 가입 Role은 VIEWER로 고정하고
/// Role ID를 별도로 보관하지 않는다. 기존 OWNER·활성 멤버의 반복 수락 판정, Viewer Role 조회와
/// Membership 생성, 권한·계정·Space 상태 확인, 트랜잭션은 호출 UseCase가 검증한다.
/// </summary>
public sealed class SpaceInvite
{
    /// <summary>내부 PK. 신규 객체는 0이며 외부 응답에 노출하지 않는다.</summary>
    public long Id { get; private set; }

    /// <summary>생성 후 변경하지 않는 UUIDv7 외부 식별자. 초대 관리 API에서 사용한다.</summary>
    public Guid PublicId { get; private set; }

    /// <summary>초대 대상 Space의 내부 FK.</summary>
    public long SpaceId { get; private set; }

    /// <summary>초대 링크를 만든 User의 내부 FK.</summary>
    public long CreatedByUserId { get; private set; }

    /// <summary>검증용 초대 token hash. 원문 token과 초대 URL은 보관하지 않는다.</summary>
    public TokenHash TokenHash { get; private set; } = null!;

    /// <summary>초대 수명주기 상태. REVOKED·EXPIRED는 terminal이다.</summary>
    public SpaceInviteStatus Status { get; private set; }

    /// <summary>만료 시각. UTC 필수 값이며 수락 경계는 <c>now &gt;= ExpiresAt</c>이다.</summary>
    public DateTimeOffset ExpiresAt { get; private set; }

    /// <summary>지금까지 수락에 사용된 횟수.</summary>
    public int UseCount { get; private set; }

    /// <summary>최대 사용 횟수. null은 제한 없음이다.</summary>
    public int? MaxUses { get; private set; }

    /// <summary>
    /// 초대 수락으로 신규 가입하는 사용자에게 부여하는 기본 Role의 시스템 식별자.
    /// 신규 가입 Role은 VIEWER로 고정하며 생성·복원 모두 Role 입력을 받지 않는다.
    /// </summary>
    public SpaceRoleSystemKey GrantedRole => SpaceRoleSystemKey.Viewer;

    public long Version { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>REVOKED에서만 존재하는 폐기 시각.</summary>
    public DateTimeOffset? RevokedAt { get; private set; }

    private SpaceInvite() { }

    /// <summary>
    /// 새 ACTIVE 초대 링크를 만든다. 만료 시각은 UTC now보다 엄격히 미래여야 하고 최대 사용 횟수는
    /// 양수 또는 null(무제한)이다. 초대 생성자의 InviteMember 권한과 Space·계정 상태는 호출 UseCase가
    /// 검증한다.
    /// </summary>
    public static Result<SpaceInvite> Create(
        long spaceId,
        long createdByUserId,
        TokenHash tokenHash,
        DateTimeOffset expiresAt,
        int? maxUses,
        DateTimeOffset now)
    {
        if (now.Offset != TimeSpan.Zero)
        {
            return Result.Fail<SpaceInvite>(SpaceInviteError.InvalidState("Create requires UTC now."));
        }

        var candidate = new SpaceInvite
        {
            PublicId = Guid.CreateVersion7(),
            SpaceId = spaceId,
            CreatedByUserId = createdByUserId,
            TokenHash = tokenHash!,
            Status = SpaceInviteStatus.Active,
            ExpiresAt = expiresAt,
            UseCount = 0,
            MaxUses = maxUses,
            Version = 1,
            CreatedAt = now,
            UpdatedAt = now,
        };

        var validation = new SpaceInviteCreateValidator().Validate(candidate);
        if (!validation.IsValid)
        {
            return validation.ToFailureResult<SpaceInvite>();
        }

        return Result.Ok(candidate);
    }

    /// <summary>
    /// 영속 snapshot을 손실 없이 복원한다. 잘못된 영속 상태는 프로그래밍·데이터 오류로 거부하며
    /// 복원 중 Version·시각을 변경하지 않는다. GrantedRole은 계산 속성이므로 Role 정보를 받지 않는다.
    /// </summary>
    public static SpaceInvite Reconstitute(
        long id,
        Guid publicId,
        long spaceId,
        long createdByUserId,
        TokenHash tokenHash,
        SpaceInviteStatus status,
        int useCount,
        int? maxUses,
        DateTimeOffset expiresAt,
        long version,
        DateTimeOffset createdAt,
        DateTimeOffset updatedAt,
        DateTimeOffset? revokedAt)
    {
        if (id < 0)
        {
            throw new ArgumentException("Invite id must not be negative.", nameof(id));
        }

        if (publicId == Guid.Empty)
        {
            throw new ArgumentException("Invite public id must not be empty.", nameof(publicId));
        }

        if (spaceId <= 0)
        {
            throw new ArgumentException("Invite space id must be positive.", nameof(spaceId));
        }

        if (createdByUserId <= 0)
        {
            throw new ArgumentException("Invite creator user id must be positive.", nameof(createdByUserId));
        }

        ArgumentNullException.ThrowIfNull(tokenHash);

        if (!Enum.IsDefined(status))
        {
            throw new ArgumentOutOfRangeException(nameof(status), status, "Unsupported invite status.");
        }

        if (useCount < 0)
        {
            throw new ArgumentException("Invite use count must not be negative.", nameof(useCount));
        }

        if (maxUses is < 1)
        {
            throw new ArgumentException("Invite max uses must be positive.", nameof(maxUses));
        }

        if (useCount > maxUses)
        {
            throw new ArgumentException("Invite use count must not exceed max uses.", nameof(useCount));
        }

        if (version < 1)
        {
            throw new ArgumentException("Version must be positive.", nameof(version));
        }

        if (createdAt.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException("CreatedAt must be UTC.", nameof(createdAt));
        }

        if (expiresAt.Offset != TimeSpan.Zero || expiresAt <= createdAt)
        {
            throw new ArgumentException("ExpiresAt must be UTC and after CreatedAt.", nameof(expiresAt));
        }

        ValidateSnapshotTimestamp(updatedAt, nameof(updatedAt), createdAt, updatedAt);
        ValidateSnapshotTimestamp(revokedAt, nameof(revokedAt), createdAt, updatedAt);

        if ((status == SpaceInviteStatus.Revoked) != revokedAt.HasValue)
        {
            throw new ArgumentException("RevokedAt must be present only for REVOKED status.", nameof(revokedAt));
        }

        return new SpaceInvite
        {
            Id = id,
            PublicId = publicId,
            SpaceId = spaceId,
            CreatedByUserId = createdByUserId,
            TokenHash = tokenHash,
            Status = status,
            ExpiresAt = expiresAt,
            UseCount = useCount,
            MaxUses = maxUses,
            Version = version,
            CreatedAt = createdAt,
            UpdatedAt = updatedAt,
            RevokedAt = revokedAt,
        };
    }

    /// <summary>
    /// 수락 가능 여부를 변경 없이 판단한다. 오류는 폐기 → 사용 한도 소진 → 만료 순서로 반환하며,
    /// 사용 한도로 EXPIRED가 된 초대는 계속 사용 한도 오류를 반환한다.
    /// </summary>
    public Result CanAccept(DateTimeOffset now)
    {
        if (Status == SpaceInviteStatus.Revoked)
        {
            return Result.Fail(SpaceInviteError.Revoked());
        }

        if (MaxUses is int maxUses && UseCount >= maxUses)
        {
            return Result.Fail(SpaceInviteError.UsageExhausted());
        }

        if (Status == SpaceInviteStatus.Expired)
        {
            return Result.Fail(SpaceInviteError.Expired());
        }

        var validation = ValidateNow(now);
        if (validation.IsFailed)
        {
            return validation;
        }

        return now >= ExpiresAt
            ? Result.Fail(SpaceInviteError.Expired())
            : Result.Ok();
    }

    /// <summary>
    /// 수락 한 건을 기록해 사용 횟수를 증가시킨다. 사용 한도의 마지막 허용 수락은 성공과 함께
    /// EXPIRED로 전환한다. 중복 수락 판정과 Viewer Role 조회, Membership 생성은 호출 UseCase가 검증한다.
    /// </summary>
    public Result RecordAcceptance(DateTimeOffset now)
    {
        var canAccept = CanAccept(now);
        if (canAccept.IsFailed)
        {
            return canAccept;
        }

        if (UseCount == int.MaxValue)
        {
            return Result.Fail(SpaceInviteError.InvalidState("Invite use count cannot be advanced further."));
        }

        return ApplyChange(now, () =>
        {
            UseCount++;
            if (MaxUses is int maxUses && UseCount >= maxUses)
            {
                Status = SpaceInviteStatus.Expired;
            }
        });
    }

    /// <summary>ACTIVE 초대 링크를 폐기해 이후 수락을 막는다. 이미 참여한 멤버는 멤버 관리 대상이다.</summary>
    public Result Revoke(DateTimeOffset now)
    {
        if (Status != SpaceInviteStatus.Active)
        {
            return Result.Fail(SpaceInviteError.InvalidState("Only ACTIVE invites can be revoked."));
        }

        var validation = ValidateNow(now);
        if (validation.IsFailed)
        {
            return validation;
        }

        return ApplyChange(now, () =>
        {
            Status = SpaceInviteStatus.Revoked;
            RevokedAt = now;
        });
    }

    /// <summary>
    /// 만료 시각 또는 사용 한도에 도달한 ACTIVE 초대 링크를 EXPIRED로 전환한다.
    /// 아직 조건에 도달하지 않은 조기 전환은 거부한다.
    /// </summary>
    public Result Expire(DateTimeOffset now)
    {
        if (Status != SpaceInviteStatus.Active)
        {
            return Result.Fail(SpaceInviteError.InvalidState("Only ACTIVE invites can expire."));
        }

        var validation = ValidateNow(now);
        if (validation.IsFailed)
        {
            return validation;
        }

        if (now < ExpiresAt && !(MaxUses is int maxUses && UseCount >= maxUses))
        {
            return Result.Fail(
                SpaceInviteError.InvalidState("Invite has not reached its expiry time or usage limit."));
        }

        return ApplyChange(now, () => Status = SpaceInviteStatus.Expired);
    }

    private Result ValidateNow(DateTimeOffset now)
    {
        if (now.Offset != TimeSpan.Zero || now < UpdatedAt)
        {
            return Result.Fail(
                SpaceInviteError.InvalidState("Operation requires UTC now at or after UpdatedAt."));
        }

        return Result.Ok();
    }

    private Result ApplyChange(DateTimeOffset now, Action change)
    {
        if (Version == long.MaxValue)
        {
            return Result.Fail(SpaceInviteError.InvalidState("Invite version cannot be advanced further."));
        }

        change();
        Version++;
        UpdatedAt = now;
        return Result.Ok();
    }

    private static void ValidateSnapshotTimestamp(
        DateTimeOffset? value,
        string parameterName,
        DateTimeOffset createdAt,
        DateTimeOffset updatedAt)
    {
        if (value is DateTimeOffset timestamp
            && (timestamp.Offset != TimeSpan.Zero || timestamp < createdAt || timestamp > updatedAt))
        {
            throw new ArgumentException("Timestamp must be UTC and between CreatedAt and UpdatedAt.", parameterName);
        }
    }
}

internal sealed class SpaceInviteCreateValidator : AbstractValidator<SpaceInvite>
{
    public SpaceInviteCreateValidator()
    {
        RuleFor(invite => invite.SpaceId)
            .GreaterThan(0)
            .WithErrorCode(ErrorCodes.SpaceInvite.SpaceIdInvalid)
            .WithMessage("Invite space id must be positive.");

        RuleFor(invite => invite.CreatedByUserId)
            .GreaterThan(0)
            .WithErrorCode(ErrorCodes.SpaceInvite.CreatedByInvalid)
            .WithMessage("Invite creator user id must be positive.");

        RuleFor(invite => invite.TokenHash)
            .NotNull()
            .WithErrorCode(ErrorCodes.SpaceInvite.TokenHashInvalid)
            .WithMessage("Invite token hash is required.");

        RuleFor(invite => invite.ExpiresAt)
            .Must(expiresAt => expiresAt.Offset == TimeSpan.Zero)
            .WithErrorCode(ErrorCodes.SpaceInvite.ExpiresAtInvalid)
            .WithMessage("Invite expiry must be UTC.")
            .Must((invite, expiresAt) => expiresAt > invite.CreatedAt)
            .WithErrorCode(ErrorCodes.SpaceInvite.ExpiresAtInvalid)
            .WithMessage("Invite expiry must be in the future.");

        RuleFor(invite => invite.MaxUses)
            .Must(maxUses => maxUses is null || maxUses > 0)
            .WithErrorCode(ErrorCodes.SpaceInvite.MaxUsesInvalid)
            .WithMessage("Invite max uses must be positive when provided.");
    }
}