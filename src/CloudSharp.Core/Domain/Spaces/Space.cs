using CloudSharp.Core.Common.Errors;
using CloudSharp.Core.Common.Results;
using FluentResults;
using FluentValidation;

namespace CloudSharp.Core.Domain.Spaces;

/// <summary>
/// 독립된 파일공간의 소유자, 수명주기와 용량 집계를 관리하는 Aggregate.
/// 사용자 활성 상태·Membership·권한 조회와 트랜잭션은 UseCase에서 검증한다.
/// </summary>
public sealed class Space
{
    /// <summary>내부 PK. 신규 객체는 0이며 외부 응답에 노출하지 않는다.</summary>
    public long Id { get; private set; }

    /// <summary>생성 후 변경하지 않는 UUIDv7 외부 식별자.</summary>
    public Guid PublicId { get; private set; }

    /// <summary>단일 소유자. OWNER를 일반 Membership Role로 저장하지 않는다.</summary>
    public long OwnerUserId { get; private set; }

    public string Name { get; private set; } = null!;
    public string? Description { get; private set; }
    public SpaceStatus Status { get; private set; }

    /// <summary>null은 무제한이며 0은 업로드 여유가 없는 공간이다.</summary>
    public long? AllowedBytes { get; private set; }

    public long UsedBytes { get; private set; }
    public long ReservedBytes { get; private set; }

    /// <summary>추가 예약 가능한 용량. 무제한이면 null이다.</summary>
    public long? AvailableBytes => AllowedBytes is long limit ? limit - UsedBytes - ReservedBytes : null;

    public long Version { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public DateTimeOffset? ArchivedAt { get; private set; }
    public DateTimeOffset? DeletingAt { get; private set; }
    public DateTimeOffset? DeletedAt { get; private set; }

    private Space() { }

    /// <summary>
    /// 새 ACTIVE Space를 생성한다. 소유자가 활성 User인지는 호출 UseCase가 검증하고,
    /// 기본 Role과 루트 폴더도 같은 생성 트랜잭션에서 준비한다.
    /// </summary>
    public static Result<Space> Create(
        long ownerUserId,
        string name,
        string? description,
        long? allowedBytes,
        DateTimeOffset now)
    {
        if (ownerUserId <= 0)
        {
            return Result.Fail<Space>(SpaceError.OwnerInvalid());
        }

        if (now.Offset != TimeSpan.Zero)
        {
            return Result.Fail<Space>(SpaceError.InvalidState("Create requires UTC now."));
        }

        var nameValidation = ValidateName(name);
        if (nameValidation.IsFailed)
        {
            return Result.Fail<Space>(nameValidation.Errors);
        }

        var quotaValidation = ValidateQuota(allowedBytes);
        if (quotaValidation.IsFailed)
        {
            return Result.Fail<Space>(quotaValidation.Errors);
        }

        return Result.Ok(new Space
        {
            PublicId = Guid.CreateVersion7(),
            OwnerUserId = ownerUserId,
            Name = name.Trim(),
            Description = NormalizeDescription(description),
            Status = SpaceStatus.Active,
            AllowedBytes = allowedBytes,
            Version = 1,
            CreatedAt = now,
            UpdatedAt = now,
        });
    }

    /// <summary>
    /// 영속 snapshot을 손실 없이 복원한다. 잘못된 영속 상태는 프로그래밍·데이터 오류로 거부한다.
    /// 이름·설명을 다시 정규화하거나 현재 시각을 사용하지 않는다.
    /// </summary>
    public static Space Reconstitute(
        long id,
        Guid publicId,
        long ownerUserId,
        string name,
        string? description,
        SpaceStatus status,
        long? allowedBytes,
        long usedBytes,
        long reservedBytes,
        long version,
        DateTimeOffset createdAt,
        DateTimeOffset updatedAt,
        DateTimeOffset? archivedAt,
        DateTimeOffset? deletingAt,
        DateTimeOffset? deletedAt)
    {
        if (id < 0)
        {
            throw new ArgumentException("Space id must not be negative.", nameof(id));
        }

        if (publicId == Guid.Empty)
        {
            throw new ArgumentException("Space public id must not be empty.", nameof(publicId));
        }

        if (ownerUserId <= 0)
        {
            throw new ArgumentException("Space owner id must be positive.", nameof(ownerUserId));
        }

        if (string.IsNullOrWhiteSpace(name) || name.Length > 200)
        {
            throw new ArgumentException("Space name must be non-blank and at most 200 characters.", nameof(name));
        }

        if (!Enum.IsDefined(status))
        {
            throw new ArgumentOutOfRangeException(nameof(status), status, "Unsupported space status.");
        }

        if (allowedBytes is < 0)
        {
            throw new ArgumentException("Allowed storage must not be negative.", nameof(allowedBytes));
        }

        if (usedBytes < 0)
        {
            throw new ArgumentException("Used storage must not be negative.", nameof(usedBytes));
        }

        if (reservedBytes < 0 || usedBytes > long.MaxValue - reservedBytes)
        {
            throw new ArgumentException("Reserved storage is negative or exceeds the storage counter range.", nameof(reservedBytes));
        }

        if (allowedBytes is long limit && usedBytes + reservedBytes > limit)
        {
            throw new ArgumentException("Used and reserved storage exceed quota.", nameof(allowedBytes));
        }

        if (version < 1)
        {
            throw new ArgumentException("Version must be positive.", nameof(version));
        }

        if (createdAt.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException("CreatedAt must be UTC.", nameof(createdAt));
        }

        ValidateSnapshotTimestamp(updatedAt, nameof(updatedAt), createdAt, updatedAt);
        ValidateSnapshotTimestamp(archivedAt, nameof(archivedAt), createdAt, updatedAt);
        ValidateSnapshotTimestamp(deletingAt, nameof(deletingAt), createdAt, updatedAt);
        ValidateSnapshotTimestamp(deletedAt, nameof(deletedAt), createdAt, updatedAt);

        if ((status == SpaceStatus.Archived) != archivedAt.HasValue)
        {
            throw new ArgumentException("ArchivedAt must be present only for ARCHIVED status.", nameof(archivedAt));
        }

        if ((status is SpaceStatus.Deleting or SpaceStatus.Deleted) != deletingAt.HasValue)
        {
            throw new ArgumentException("DeletingAt is required for DELETING and DELETED status only.", nameof(deletingAt));
        }

        if ((status == SpaceStatus.Deleted) != deletedAt.HasValue || deletedAt < deletingAt)
        {
            throw new ArgumentException("DeletedAt is required only for DELETED and must follow DeletingAt.", nameof(deletedAt));
        }

        if (status == SpaceStatus.Deleted && (usedBytes != 0 || reservedBytes != 0))
        {
            throw new ArgumentException("Deleted spaces must have no used or reserved storage.", nameof(status));
        }

        return new Space
        {
            Id = id,
            PublicId = publicId,
            OwnerUserId = ownerUserId,
            Name = name,
            Description = description,
            Status = status,
            AllowedBytes = allowedBytes,
            UsedBytes = usedBytes,
            ReservedBytes = reservedBytes,
            Version = version,
            CreatedAt = createdAt,
            UpdatedAt = updatedAt,
            ArchivedAt = archivedAt,
            DeletingAt = deletingAt,
            DeletedAt = deletedAt,
        };
    }

    /// <summary>ACTIVE Space의 이름·설명을 변경한다. 같은 값이면 버전과 시각을 유지한다.</summary>
    public Result UpdateMetadata(string name, string? description, DateTimeOffset now)
    {
        var validation = EnsureActive(now);
        if (validation.IsFailed)
        {
            return validation;
        }

        validation = ValidateName(name);
        if (validation.IsFailed)
        {
            return validation;
        }

        var normalizedName = name.Trim();
        var normalizedDescription = NormalizeDescription(description);
        if (Name == normalizedName && Description == normalizedDescription)
        {
            return Result.Ok();
        }

        return ApplyChange(now, () =>
        {
            Name = normalizedName;
            Description = normalizedDescription;
        });
    }

    /// <summary>사용량과 예약량의 합 이상으로 quota를 변경한다. null은 무제한이다.</summary>
    public Result ChangeQuota(long? allowedBytes, DateTimeOffset now)
    {
        var validation = EnsureActive(now);
        if (validation.IsFailed)
        {
            return validation;
        }

        validation = ValidateQuota(allowedBytes);
        if (validation.IsFailed)
        {
            return validation;
        }

        if (allowedBytes is long limit && UsedBytes + ReservedBytes > limit)
        {
            return Result.Fail(SpaceError.QuotaTooSmall());
        }

        return AllowedBytes == allowedBytes
            ? Result.Ok()
            : ApplyChange(now, () => AllowedBytes = allowedBytes);
    }

    /// <summary>신규 업로드 용량을 예약한다. 무제한 quota도 long 집계 범위를 넘길 수 없다.</summary>
    public Result ReserveStorage(long bytes, DateTimeOffset now)
    {
        var validation = EnsureActive(now);
        if (validation.IsFailed)
        {
            return validation;
        }

        validation = ValidateStorageAmount(bytes);
        if (validation.IsFailed)
        {
            return validation;
        }

        if (bytes > long.MaxValue - UsedBytes - ReservedBytes
            || (AllowedBytes is long limit && bytes > limit - UsedBytes - ReservedBytes))
        {
            return Result.Fail(SpaceError.QuotaExceeded());
        }

        return ApplyChange(now, () => ReservedBytes += bytes);
    }

    /// <summary>
    /// 취소·만료된 예약을 해제한다. 보관·삭제 중에도 기존 예약의 정리를 허용한다.
    /// 어떤 예약이 해제되는지와 중복 처리는 FileReservation 및 UseCase가 검증한다.
    /// </summary>
    public Result ReleaseReservation(long bytes, DateTimeOffset now)
    {
        var validation = EnsureCleanupAllowed(now);
        if (validation.IsFailed)
        {
            return validation;
        }

        validation = ValidateStorageAmount(bytes);
        if (validation.IsFailed)
        {
            return validation;
        }

        if (bytes > ReservedBytes)
        {
            return Result.Fail(SpaceError.ReservationInvalid());
        }

        return ApplyChange(now, () => ReservedBytes -= bytes);
    }

    /// <summary>ACTIVE Space에서 예약량을 확정 사용량으로 옮긴다. 총 점유량은 유지된다.</summary>
    public Result ConsumeReservation(long bytes, DateTimeOffset now)
    {
        var validation = EnsureActive(now);
        if (validation.IsFailed)
        {
            return validation;
        }

        validation = ValidateStorageAmount(bytes);
        if (validation.IsFailed)
        {
            return validation;
        }

        if (bytes > ReservedBytes)
        {
            return Result.Fail(SpaceError.ReservationInvalid());
        }

        return ApplyChange(now, () =>
        {
            ReservedBytes -= bytes;
            UsedBytes += bytes;
        });
    }

    /// <summary>
    /// 영구 삭제된 파일의 사용량을 차감한다. 휴지통 이동만으로 호출하지 않는다.
    /// 보관·삭제 중인 Space에서도 실제 정리와 같은 트랜잭션으로 반영할 수 있다.
    /// </summary>
    public Result ReleaseUsedStorage(long bytes, DateTimeOffset now)
    {
        var validation = EnsureCleanupAllowed(now);
        if (validation.IsFailed)
        {
            return validation;
        }

        validation = ValidateStorageAmount(bytes);
        if (validation.IsFailed)
        {
            return validation;
        }

        if (bytes > UsedBytes)
        {
            return Result.Fail(SpaceError.StorageAmountInvalid());
        }

        return ApplyChange(now, () => UsedBytes -= bytes);
    }

    /// <summary>ACTIVE Space를 읽기 전용으로 보관한다.</summary>
    public Result Archive(DateTimeOffset now)
    {
        if (Status != SpaceStatus.Active)
        {
            return Result.Fail(SpaceError.InvalidState("Only ACTIVE spaces can be archived."));
        }

        var validation = ValidateNow(now);
        if (validation.IsFailed)
        {
            return validation;
        }

        return ApplyChange(now, () =>
        {
            Status = SpaceStatus.Archived;
            ArchivedAt = now;
        });
    }

    /// <summary>보관된 Space를 재활성화한다.</summary>
    public Result Reactivate(DateTimeOffset now)
    {
        if (Status != SpaceStatus.Archived)
        {
            return Result.Fail(SpaceError.InvalidState("Only ARCHIVED spaces can be reactivated."));
        }

        var validation = ValidateNow(now);
        if (validation.IsFailed)
        {
            return validation;
        }

        return ApplyChange(now, () =>
        {
            Status = SpaceStatus.Active;
            ArchivedAt = null;
        });
    }

    /// <summary>삭제를 시작해 신규 쓰기를 막는다. 실제 리소스 정리 전까지 용량 집계는 유지한다.</summary>
    public Result BeginDelete(DateTimeOffset now)
    {
        if (Status is not (SpaceStatus.Active or SpaceStatus.Archived))
        {
            return Result.Fail(SpaceError.InvalidState("Only ACTIVE or ARCHIVED spaces can begin deletion."));
        }

        var validation = ValidateNow(now);
        if (validation.IsFailed)
        {
            return validation;
        }

        return ApplyChange(now, () =>
        {
            Status = SpaceStatus.Deleting;
            ArchivedAt = null;
            DeletingAt = now;
        });
    }

    /// <summary>사용·예약 용량의 정리가 끝난 DELETING Space를 최종 삭제 상태로 전환한다.</summary>
    public Result CompleteDelete(DateTimeOffset now)
    {
        if (Status != SpaceStatus.Deleting)
        {
            return Result.Fail(SpaceError.InvalidState("Only DELETING spaces can complete deletion."));
        }

        var validation = ValidateNow(now);
        if (validation.IsFailed)
        {
            return validation;
        }

        if (UsedBytes != 0 || ReservedBytes != 0)
        {
            return Result.Fail(SpaceError.InvalidState("Used and reserved storage must be cleared before deletion completes."));
        }

        return ApplyChange(now, () =>
        {
            Status = SpaceStatus.Deleted;
            DeletedAt = now;
        });
    }

    /// <summary>
    /// 소유자를 교체한다. 동일한 소유자는 멱등 성공한다. 새 소유자의 활성 계정·동일 Space 활성
    /// Membership 확인, 새 소유자 Membership 제거와 이전 소유자 ADMIN 부여는 UseCase의 원자 작업이다.
    /// </summary>
    public Result TransferOwnership(long newOwnerUserId, DateTimeOffset now)
    {
        var validation = EnsureActive(now);
        if (validation.IsFailed)
        {
            return validation;
        }

        if (newOwnerUserId <= 0)
        {
            return Result.Fail(SpaceError.TransferTargetInvalid());
        }

        return OwnerUserId == newOwnerUserId
            ? Result.Ok()
            : ApplyChange(now, () => OwnerUserId = newOwnerUserId);
    }

    private Result EnsureActive(DateTimeOffset now) =>
        Status == SpaceStatus.Active ? ValidateNow(now) : Result.Fail(SpaceError.Inactive());

    private Result EnsureCleanupAllowed(DateTimeOffset now) =>
        Status == SpaceStatus.Deleted ? Result.Fail(SpaceError.Inactive()) : ValidateNow(now);

    private Result ValidateNow(DateTimeOffset now)
    {
        if (now.Offset != TimeSpan.Zero || now < UpdatedAt)
        {
            return Result.Fail(SpaceError.InvalidState("Operation requires UTC now at or after UpdatedAt."));
        }

        return Result.Ok();
    }

    private Result ApplyChange(DateTimeOffset now, Action change)
    {
        if (Version == long.MaxValue)
        {
            return Result.Fail(SpaceError.InvalidState("Space version cannot be advanced further."));
        }

        change();
        Version++;
        UpdatedAt = now;
        return Result.Ok();
    }

    private static Result ValidateName(string? name)
    {
        if (name is null)
        {
            return Result.Fail(SpaceError.NameInvalid());
        }

        var validation = new SpaceNameValidator().Validate(name);
        return validation.IsValid ? Result.Ok() : validation.ToFailureResult();
    }

    private static Result ValidateQuota(long? allowedBytes)
    {
        if (allowedBytes is null)
        {
            return Result.Ok();
        }

        var validation = new SpaceQuotaValidator().Validate(allowedBytes.Value);
        return validation.IsValid ? Result.Ok() : validation.ToFailureResult();
    }

    private static Result ValidateStorageAmount(long bytes)
    {
        var validation = new SpaceStorageAmountValidator().Validate(bytes);
        return validation.IsValid ? Result.Ok() : validation.ToFailureResult();
    }

    private static string? NormalizeDescription(string? description) =>
        string.IsNullOrWhiteSpace(description) ? null : description.Trim();

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

internal sealed class SpaceNameValidator : AbstractValidator<string>
{
    public SpaceNameValidator()
    {
        RuleFor(x => x)
            .NotEmpty()
            .WithErrorCode(ErrorCodes.Space.NameInvalid)
            .WithMessage("Space name is required.")
            .Must(value => value is null || value.Trim().Length <= 200)
            .WithErrorCode(ErrorCodes.Space.NameInvalid)
            .WithMessage("Space name must not exceed 200 characters.");
    }
}

internal sealed class SpaceQuotaValidator : AbstractValidator<long>
{
    public SpaceQuotaValidator()
    {
        RuleFor(x => x)
            .GreaterThanOrEqualTo(0)
            .WithErrorCode(ErrorCodes.Space.QuotaInvalid)
            .WithMessage("Space quota must not be negative.");
    }
}

internal sealed class SpaceStorageAmountValidator : AbstractValidator<long>
{
    public SpaceStorageAmountValidator()
    {
        RuleFor(x => x)
            .GreaterThan(0)
            .WithErrorCode(ErrorCodes.Space.StorageAmountInvalid)
            .WithMessage("Storage amount must be positive.");
    }
}
