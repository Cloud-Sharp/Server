using CloudSharp.Core.Common.Errors;
using CloudSharp.Core.Common.Results;
using FluentResults;
using FluentValidation;

namespace CloudSharp.Core.Domain.Users;

/// <summary>
/// 로그인 가능한 계정 Aggregate. 식별 정보, 프로필, 시스템 상태, 보안 버전을 소유한다.
/// 비밀번호 원문, 세션, Space 권한은 소유하지 않는다. 모든 상태 전이는 행위 중심 메서드로 처리한다.
/// </summary>
public sealed class User
{
    /// <summary>
    /// 내부 PK. 외부 응답과 이벤트에 노출하지 않는다.
    /// </summary>
    public long Id { get; private set; }

    /// <summary>
    /// 외부 식별자. UUIDv7, 생성 후 불변.
    /// </summary>
    public Guid PublicId { get; private set; }

    /// <summary>
    /// 정규화된 고유 이메일.
    /// </summary>
    public EmailAddress Email { get; private set; } = null!;

    /// <summary>
    /// 대소문자 비구분 고유 사용자명. 선택값.
    /// </summary>
    public NormalizedName? UserName { get; private set; }

    /// <summary>
    /// 표시명. 최대 100자, <c>null</c>로 지울 수 있다.
    /// </summary>
    public string? DisplayName { get; private set; }

    /// <summary>
    /// 승인된 password hasher 결과. 원문 비밀번호는 여기에 들어오지 않는다.
    /// </summary>
    public string PasswordHash { get; private set; } = null!;

    /// <summary>
    /// 시스템 전역 역할.
    /// </summary>
    public SystemRole SystemRole { get; private set; }

    /// <summary>
    /// 계정 상태.
    /// </summary>
    public UserStatus Status { get; private set; }

    /// <summary>
    /// 전체 세션 무효화 버전. 비밀번호 변경·정지·삭제·전체 세션 폐기 시 증가한다.
    /// </summary>
    public long SecurityVersion { get; private set; }

    /// <summary>
    /// optimistic concurrency version.
    /// </summary>
    public long Version { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public DateTimeOffset? SuspendedAt { get; private set; }

    public DateTimeOffset? DeletedAt { get; private set; }

    private User() { }

    /// <summary>
    /// 새 사용자를 생성한다. UUIDv7 <see cref="PublicId"/>, <see cref="UserStatus.Active"/>,
    /// <see cref="SecurityVersion"/>=1, <see cref="Version"/>=1로 시작한다.
    /// </summary>
    public static Result<User> Create(
        string email,
        string? userName,
        string? displayName,
        string passwordHash,
        DateTimeOffset now)
    {
        if (now.Offset != TimeSpan.Zero)
        {
            return Result.Fail<User>(UserError.InvalidState("Create requires UTC now."));
        }

        var emailResult = EmailAddress.Create(email);
        if (emailResult.IsFailed)
        {
            return Result.Fail<User>(emailResult.Errors);
        }

        NormalizedName? normalizedName = null;
        if (userName is not null)
        {
            var userNameResult = NormalizedName.Create(userName);
            if (userNameResult.IsFailed)
            {
                return Result.Fail<User>(userNameResult.Errors);
            }
            normalizedName = userNameResult.Value;
        }

        var displayNameResult = ValidateDisplayName(displayName);
        if (displayNameResult.IsFailed)
        {
            return Result.Fail<User>(displayNameResult.Errors);
        }

        if (passwordHash is null)
        {
            return Result.Fail<User>(UserError.PasswordInvalid("Password hash is required."));
        }

        var passwordHashResult = ValidatePasswordHash(passwordHash);
        if (passwordHashResult.IsFailed)
        {
            return Result.Fail<User>(passwordHashResult.Errors);
        }

        var user = new User
        {
            PublicId = Guid.CreateVersion7(),
            Email = emailResult.Value,
            UserName = normalizedName,
            DisplayName = NormalizeDisplayName(displayName),
            PasswordHash = passwordHash.Trim(),
            SystemRole = SystemRole.User,
            Status = UserStatus.Active,
            SecurityVersion = 1,
            Version = 1,
            CreatedAt = now,
            UpdatedAt = now,
        };

        return Result.Ok(user);
    }

    /// <summary>
    /// Infrastructure 복원 전용. 신규 validation은 수행하지 않고 ID, enum, 버전, 필수 값과 상태별 timestamp 일관성만 방어 검증한다.
    /// </summary>
    public static User Reconstitute(
        long id,
        Guid publicId,
        EmailAddress email,
        NormalizedName? userName,
        string? displayName,
        string passwordHash,
        SystemRole systemRole,
        UserStatus status,
        long securityVersion,
        long version,
        DateTimeOffset createdAt,
        DateTimeOffset updatedAt,
        DateTimeOffset? suspendedAt,
        DateTimeOffset? deletedAt)
    {
        if (id < 0)
        {
            throw new ArgumentException("User id must not be negative.", nameof(id));
        }

        if (publicId == Guid.Empty)
        {
            throw new ArgumentException("User public id must not be empty.", nameof(publicId));
        }

        ArgumentNullException.ThrowIfNull(email);
        if (!Enum.IsDefined(systemRole))
        {
            throw new ArgumentOutOfRangeException(nameof(systemRole), systemRole, "Unsupported system role.");
        }

        if (!Enum.IsDefined(status))
        {
            throw new ArgumentOutOfRangeException(nameof(status), status, "Unsupported user status.");
        }

        if (string.IsNullOrWhiteSpace(passwordHash))
        {
            throw new ArgumentException("Password hash must not be empty.", nameof(passwordHash));
        }

        if (securityVersion < 1)
        {
            throw new ArgumentException("SecurityVersion must be positive.", nameof(securityVersion));
        }

        if (version < 1)
        {
            throw new ArgumentException("Version must be positive.", nameof(version));
        }

        if (createdAt.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException("CreatedAt must be UTC.", nameof(createdAt));
        }

        if (updatedAt.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException("UpdatedAt must be UTC.", nameof(updatedAt));
        }

        if (updatedAt < createdAt)
        {
            throw new ArgumentException("UpdatedAt must not be earlier than CreatedAt.", nameof(updatedAt));
        }

        if (displayName is not null && displayName.Length > 100)
        {
            throw new ArgumentException("Display name length must not exceed 100.", nameof(displayName));
        }

        if (status == UserStatus.Suspended && suspendedAt is null)
        {
            throw new ArgumentException("SuspendedAt is required for SUSPENDED status.", nameof(suspendedAt));
        }

        if (status == UserStatus.Deleted && deletedAt is null)
        {
            throw new ArgumentException("DeletedAt is required for DELETED status.", nameof(deletedAt));
        }

        if (status != UserStatus.Suspended && suspendedAt is not null)
        {
            throw new ArgumentException("SuspendedAt must be null unless status is SUSPENDED.", nameof(suspendedAt));
        }

        if (status != UserStatus.Deleted && deletedAt is not null)
        {
            throw new ArgumentException("DeletedAt must be null unless status is DELETED.", nameof(deletedAt));
        }

        if (suspendedAt is not null && suspendedAt.Value.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException("SuspendedAt must be UTC.", nameof(suspendedAt));
        }

        if (deletedAt is not null && deletedAt.Value.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException("DeletedAt must be UTC.", nameof(deletedAt));
        }

        return new User
        {
            Id = id,
            PublicId = publicId,
            Email = email,
            UserName = userName,
            DisplayName = displayName,
            PasswordHash = passwordHash,
            SystemRole = systemRole,
            Status = status,
            SecurityVersion = securityVersion,
            Version = version,
            CreatedAt = createdAt,
            UpdatedAt = updatedAt,
            SuspendedAt = suspendedAt,
            DeletedAt = deletedAt,
        };
    }

    /// <summary>
    /// 로그인 가능 여부. <see cref="UserStatus.Active"/> 계정만 로그인할 수 있다.
    /// 정지·삭제된 계정은 로그인 시도에서 일반 자격 증명 오류와 구분되지 않게 거부된다.
    /// </summary>
    public bool CanLogin() => Status == UserStatus.Active;

    /// <summary>
    /// 표시명을 변경한다. 동일한 값은 no-op으로 처리하고 버전을 올리지 않는다.
    /// <c>null</c>을 전달해 표시명을 지울 수 있다. <see cref="UserStatus.Deleted"/> 상태에서는 거부한다.
    /// </summary>
    public Result ChangeProfile(string? displayName, DateTimeOffset now)
    {
        if (Status == UserStatus.Deleted)
        {
            return Result.Fail(UserError.Deleted());
        }

        if (now.Offset != TimeSpan.Zero)
        {
            return Result.Fail(UserError.InvalidState("ChangeProfile requires UTC now."));
        }

        var validation = ValidateDisplayName(displayName);
        if (validation.IsFailed)
        {
            return validation;
        }

        var newDisplayName = NormalizeDisplayName(displayName);
        if (AreEqual(newDisplayName, DisplayName))
        {
            return Result.Ok();
        }

        DisplayName = newDisplayName;
        BumpVersion(now);
        return Result.Ok();
    }

    /// <summary>
    /// 비밀번호 해시를 교체하고 <see cref="SecurityVersion"/>과 <see cref="Version"/>을 증가시킨다.
    /// </summary>
    public Result ChangePasswordHash(string passwordHash, DateTimeOffset now)
    {
        if (Status == UserStatus.Deleted)
        {
            return Result.Fail(UserError.Deleted());
        }

        if (now.Offset != TimeSpan.Zero)
        {
            return Result.Fail(UserError.InvalidState("ChangePasswordHash requires UTC now."));
        }

        var validation = ValidatePasswordHash(passwordHash);
        if (validation.IsFailed)
        {
            return validation;
        }

        PasswordHash = passwordHash.Trim();
        SecurityVersion++;
        BumpVersion(now);
        return Result.Ok();
    }

    /// <summary>
    /// <see cref="UserStatus.Active"/> → <see cref="UserStatus.Suspended"/>로 전이하고 <see cref="SecurityVersion"/>을 증가시킨다.
    /// </summary>
    public Result Suspend(DateTimeOffset now)
    {
        if (Status == UserStatus.Deleted)
        {
            return Result.Fail(UserError.Deleted());
        }

        if (now.Offset != TimeSpan.Zero)
        {
            return Result.Fail(UserError.InvalidState("Suspend requires UTC now."));
        }

        if (Status != UserStatus.Active)
        {
            return Result.Fail(UserError.InvalidState("Only ACTIVE users can be suspended."));
        }

        Status = UserStatus.Suspended;
        SuspendedAt = now;
        SecurityVersion++;
        BumpVersion(now);
        return Result.Ok();
    }

    /// <summary>
    /// <see cref="UserStatus.Suspended"/> → <see cref="UserStatus.Active"/>로 전이한다.
    /// <see cref="SecurityVersion"/>은 증가하지 않는다.
    /// </summary>
    public Result Activate(DateTimeOffset now)
    {
        if (Status == UserStatus.Deleted)
        {
            return Result.Fail(UserError.Deleted());
        }

        if (now.Offset != TimeSpan.Zero)
        {
            return Result.Fail(UserError.InvalidState("Activate requires UTC now."));
        }

        if (Status != UserStatus.Suspended)
        {
            return Result.Fail(UserError.InvalidState("Only SUSPENDED users can be activated."));
        }

        Status = UserStatus.Active;
        SuspendedAt = null;
        BumpVersion(now);
        return Result.Ok();
    }

    /// <summary>
    /// <see cref="UserStatus.Active"/> 또는 <see cref="UserStatus.Suspended"/>에서 <see cref="UserStatus.Deleted"/>로 전이한다.
    /// <see cref="UserStatus.Deleted"/>는 terminal이며 <see cref="SecurityVersion"/>을 증가시킨다.
    /// </summary>
    public Result Delete(DateTimeOffset now)
    {
        if (Status == UserStatus.Deleted)
        {
            return Result.Fail(UserError.Deleted());
        }

        if (now.Offset != TimeSpan.Zero)
        {
            return Result.Fail(UserError.InvalidState("Delete requires UTC now."));
        }

        Status = UserStatus.Deleted;
        DeletedAt = now;
        SecurityVersion++;
        BumpVersion(now);
        return Result.Ok();
    }

    /// <summary>
    /// 시스템 역할을 변경한다. 동일한 역할은 no-op으로 처리하고 버전을 올리지 않는다.
    /// Role 변경은 세션을 폐기하지 않으며 다음 요청부터 적용된다.
    /// </summary>
    public Result ChangeSystemRole(SystemRole role, DateTimeOffset now)
    {
        if (Status == UserStatus.Deleted)
        {
            return Result.Fail(UserError.Deleted());
        }

        if (now.Offset != TimeSpan.Zero)
        {
            return Result.Fail(UserError.InvalidState("ChangeSystemRole requires UTC now."));
        }

        if (!Enum.IsDefined(role))
        {
            return Result.Fail(UserError.InvalidRole("Unsupported system role."));
        }

        if (role == SystemRole)
        {
            return Result.Ok();
        }

        SystemRole = role;
        BumpVersion(now);
        return Result.Ok();
    }

    /// <summary>
    /// 전체 세션을 폐기한다. <see cref="SecurityVersion"/>을 증가시켜 이후 인증이 모두 거부되게 한다.
    /// </summary>
    public Result RevokeAllSessions(DateTimeOffset now)
    {
        if (Status == UserStatus.Deleted)
        {
            return Result.Fail(UserError.Deleted());
        }

        if (now.Offset != TimeSpan.Zero)
        {
            return Result.Fail(UserError.InvalidState("RevokeAllSessions requires UTC now."));
        }

        SecurityVersion++;
        BumpVersion(now);
        return Result.Ok();
    }

    private void BumpVersion(DateTimeOffset now)
    {
        Version++;
        UpdatedAt = now;
    }

    private static Result ValidateDisplayName(string? displayName)
    {
        if (displayName is null)
        {
            return Result.Ok();
        }

        var validator = new DisplayNameValidator();
        var validationResult = validator.Validate(displayName);
        return validationResult.IsValid ? Result.Ok() : validationResult.ToFailureResult();
    }

    private static Result ValidatePasswordHash(string passwordHash)
    {
        if (passwordHash is null)
        {
            return Result.Fail(UserError.PasswordInvalid("Password hash is required."));
        }

        var validator = new PasswordHashValidator();
        var validationResult = validator.Validate(passwordHash);
        return validationResult.IsValid ? Result.Ok() : validationResult.ToFailureResult();
    }

    private static string? NormalizeDisplayName(string? displayName)
    {
        if (displayName is null)
        {
            return null;
        }

        var trimmed = displayName.Trim();
        return trimmed.Length == 0 ? null : trimmed;
    }

    private static bool AreEqual(string? a, string? b)
    {
        if (a is null && b is null)
        {
            return true;
        }

        if (a is null || b is null)
        {
            return false;
        }

        return string.Equals(a, b, StringComparison.Ordinal);
    }
}

internal sealed class DisplayNameValidator : AbstractValidator<string?>
{
    public DisplayNameValidator()
    {
        RuleFor(x => x)
            .Must(v => v is null || v.Trim().Length <= 100)
            .WithErrorCode(ErrorCodes.User.DisplayNameInvalid)
            .WithMessage("Display name length must not exceed 100.");
    }
}

internal sealed class PasswordHashValidator : AbstractValidator<string>
{
    public PasswordHashValidator()
    {
        RuleFor(x => x)
            .NotEmpty()
            .WithErrorCode(ErrorCodes.User.PasswordInvalid)
            .WithMessage("Password hash is required.")
            .Must(v => v is null || v.Trim().Length <= 512)
            .WithErrorCode(ErrorCodes.User.PasswordInvalid)
            .WithMessage("Password hash length must not exceed 512.");
    }
}