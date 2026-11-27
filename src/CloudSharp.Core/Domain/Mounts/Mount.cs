using System.Collections.Frozen;
using CloudSharp.Core.Common.Errors;
using CloudSharp.Core.Common.Results;
using CloudSharp.Core.Domain.Memberships;
using FluentResults;
using FluentValidation;

namespace CloudSharp.Core.Domain.Mounts;

public sealed class Mount
{
    private FrozenDictionary<OverwriteTarget, MountPermissionOverwrite> _overwrites =
        FrozenDictionary<OverwriteTarget, MountPermissionOverwrite>.Empty;

    public long Id { get; private set; }
    public Guid PublicId { get; private set; }
    public long SpaceId { get; private set; }
    public string Name { get; private set; } = null!;
    public MountVisibility Visibility { get; private set; }
    public MountStatus Status { get; private set; }
    public long Version { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public DateTimeOffset? ArchivedAt { get; private set; }
    public IReadOnlyCollection<MountPermissionOverwrite> Overwrites => _overwrites.Values;

    private Mount() { }

    public static Result<Mount> Create(
        long spaceId,
        string name,
        MountVisibility visibility,
        DateTimeOffset now,
        IEnumerable<MountPermissionOverwrite>? overwrites = null)
    {
        if (spaceId <= 0)
        {
            return Result.Fail<Mount>(MountError.SpaceIdInvalid());
        }

        if (now.Offset != TimeSpan.Zero)
        {
            return Result.Fail<Mount>(MountError.InvalidState("Create requires UTC now."));
        }

        var nameValidation = ValidateName(name);
        if (nameValidation.IsFailed)
        {
            return Result.Fail<Mount>(nameValidation.Errors);
        }

        var visibilityValidation = ValidateVisibility(visibility);
        if (visibilityValidation.IsFailed)
        {
            return Result.Fail<Mount>(visibilityValidation.Errors);
        }

        var overwritesByTarget = new Dictionary<OverwriteTarget, MountPermissionOverwrite>();
        if (overwrites is not null)
        {
            foreach (var overwrite in overwrites)
            {
                if (overwrite is null || overwrite.IsEmpty)
                {
                    return Result.Fail<Mount>(
                        MountError.GrantsInvalid("Mount overwrites must not contain null or empty entries."));
                }

                if (!overwritesByTarget.TryAdd(ToTarget(overwrite), overwrite))
                {
                    return Result.Fail<Mount>(MountError.GrantsInvalid("Each mount overwrite target must be unique."));
                }
            }
        }

        return Result.Ok(new Mount
        {
            PublicId = Guid.CreateVersion7(),
            SpaceId = spaceId,
            Name = name.Trim(),
            Visibility = visibility,
            Status = MountStatus.Active,
            Version = 1,
            CreatedAt = now,
            UpdatedAt = now,
            _overwrites = overwritesByTarget.ToFrozenDictionary(),
        });
    }

    public static Mount Reconstitute(
        long id,
        Guid publicId,
        long spaceId,
        string name,
        MountVisibility visibility,
        MountStatus status,
        long version,
        DateTimeOffset createdAt,
        DateTimeOffset updatedAt,
        DateTimeOffset? archivedAt,
        IEnumerable<MountPermissionOverwrite> overwrites)
    {
        if (id < 0)
        {
            throw new ArgumentException("Mount id must not be negative.", nameof(id));
        }

        if (publicId == Guid.Empty)
        {
            throw new ArgumentException("Mount public id must not be empty.", nameof(publicId));
        }

        if (spaceId <= 0)
        {
            throw new ArgumentException("Mount space id must be positive.", nameof(spaceId));
        }

        if (string.IsNullOrWhiteSpace(name) || name.Length > 200)
        {
            throw new ArgumentException("Mount name must be non-blank and at most 200 characters.", nameof(name));
        }

        if (!Enum.IsDefined(visibility))
        {
            throw new ArgumentOutOfRangeException(nameof(visibility), visibility, "Unsupported mount visibility.");
        }

        if (!Enum.IsDefined(status))
        {
            throw new ArgumentOutOfRangeException(nameof(status), status, "Unsupported mount status.");
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

        if ((status == MountStatus.Archived) != archivedAt.HasValue)
        {
            throw new ArgumentException("ArchivedAt must be present exactly for ARCHIVED status.", nameof(archivedAt));
        }

        ArgumentNullException.ThrowIfNull(overwrites);
        var overwritesByTarget = new Dictionary<OverwriteTarget, MountPermissionOverwrite>();
        foreach (var overwrite in overwrites)
        {
            if (overwrite is null || overwrite.IsEmpty)
            {
                throw new ArgumentException("Mount overwrites must not contain null or empty entries.", nameof(overwrites));
            }

            if (!overwritesByTarget.TryAdd(ToTarget(overwrite), overwrite))
            {
                throw new ArgumentException("Each mount overwrite target must be unique.", nameof(overwrites));
            }
        }

        return new Mount
        {
            Id = id,
            PublicId = publicId,
            SpaceId = spaceId,
            Name = name,
            Visibility = visibility,
            Status = status,
            Version = version,
            CreatedAt = createdAt,
            UpdatedAt = updatedAt,
            ArchivedAt = archivedAt,
            _overwrites = overwritesByTarget.ToFrozenDictionary(),
        };
    }

    public Result UpdateSettings(string? name, MountVisibility? visibility, DateTimeOffset now)
    {
        var validation = ValidateNow(now);
        if (validation.IsFailed)
        {
            return validation;
        }

        if (name is null && visibility is null)
        {
            return Result.Fail(MountError.SettingsRequired());
        }

        var normalizedName = Name;
        if (name is not null)
        {
            validation = ValidateName(name);
            if (validation.IsFailed)
            {
                return validation;
            }

            normalizedName = name.Trim();
        }

        if (visibility.HasValue)
        {
            validation = ValidateVisibility(visibility.Value);
            if (validation.IsFailed)
            {
                return validation;
            }
        }

        var nextVisibility = visibility ?? Visibility;
        if (Name == normalizedName && Visibility == nextVisibility)
        {
            return Result.Ok();
        }

        return ApplyChange(now, () =>
        {
            Name = normalizedName;
            Visibility = nextVisibility;
        });
    }

    public Result Archive(DateTimeOffset now)
    {
        if (Status != MountStatus.Active)
        {
            return Result.Fail(CommonError.RequestConflict());
        }

        var validation = ValidateNow(now);
        if (validation.IsFailed)
        {
            return validation;
        }

        return ApplyChange(now, () =>
        {
            Status = MountStatus.Archived;
            ArchivedAt = now;
        });
    }

    public Result Reactivate(DateTimeOffset now)
    {
        if (Status != MountStatus.Archived)
        {
            return Result.Fail(CommonError.RequestConflict());
        }

        var validation = ValidateNow(now);
        if (validation.IsFailed)
        {
            return validation;
        }

        return ApplyChange(now, () =>
        {
            Status = MountStatus.Active;
            ArchivedAt = null;
        });
    }

    /// <summary>대상의 덮어쓰기를 찾는다. 없으면 null이다.</summary>
    public MountPermissionOverwrite? FindOverwrite(MountOverwriteTargetType targetType, long targetId) =>
        _overwrites.GetValueOrDefault(new OverwriteTarget(targetType, targetId));

    /// <summary>
    /// 대상의 허용·거부 집합을 전체 교체한다. 둘 다 비어 있으면 덮어쓰기를 제거한다.
    /// 같은 집합으로 다시 설정하거나 없는 덮어쓰기를 제거하면 버전을 유지한다.
    /// </summary>
    public Result SetOverwrite(
        MountOverwriteTargetType targetType,
        long targetId,
        IEnumerable<SpacePermission> allow,
        IEnumerable<SpacePermission> deny,
        DateTimeOffset now)
    {
        var validation = ValidateNow(now);
        if (validation.IsFailed)
        {
            return validation;
        }

        var overwriteResult = MountPermissionOverwrite.CreateForMutation(targetType, targetId, allow, deny);
        if (overwriteResult.IsFailed)
        {
            return Result.Fail(overwriteResult.Errors);
        }

        var overwrite = overwriteResult.Value;
        var target = new OverwriteTarget(targetType, targetId);
        var hasOverwrite = _overwrites.TryGetValue(target, out var existing);

        if (overwrite.IsEmpty)
        {
            return hasOverwrite ? ApplyChange(now, () => RemoveOverwrite(target)) : Result.Ok();
        }

        if (hasOverwrite
            && existing!.Allow.SetEquals(overwrite.Allow)
            && existing.Deny.SetEquals(overwrite.Deny))
        {
            return Result.Ok();
        }

        return ApplyChange(now, () => StoreOverwrite(target, overwrite));
    }

    private Result ValidateNow(DateTimeOffset now)
    {
        if (now.Offset != TimeSpan.Zero || now < UpdatedAt)
        {
            return Result.Fail(MountError.InvalidState("Operation requires UTC now at or after UpdatedAt."));
        }

        return Result.Ok();
    }

    private Result ApplyChange(DateTimeOffset now, Action change)
    {
        if (Version == long.MaxValue)
        {
            return Result.Fail(MountError.InvalidState("Mount version cannot be advanced further."));
        }

        change();
        Version++;
        UpdatedAt = now;
        return Result.Ok();
    }

    private void StoreOverwrite(OverwriteTarget target, MountPermissionOverwrite overwrite)
    {
        var updated = _overwrites.ToDictionary(pair => pair.Key, pair => pair.Value);
        updated[target] = overwrite;
        _overwrites = updated.ToFrozenDictionary();
    }

    private void RemoveOverwrite(OverwriteTarget target)
    {
        _overwrites = _overwrites
            .Where(pair => pair.Key != target)
            .ToFrozenDictionary(pair => pair.Key, pair => pair.Value);
    }

    private static OverwriteTarget ToTarget(MountPermissionOverwrite overwrite) =>
        new(overwrite.TargetType, overwrite.TargetId);

    private static Result ValidateName(string? name)
    {
        if (name is null)
        {
            return Result.Fail(MountError.NameInvalid());
        }

        var validation = new MountNameValidator().Validate(name);
        return validation.IsValid ? Result.Ok() : validation.ToFailureResult();
    }

    private static Result ValidateVisibility(MountVisibility visibility)
    {
        var validation = new MountVisibilityValidator().Validate(visibility);
        return validation.IsValid ? Result.Ok() : validation.ToFailureResult();
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

    private readonly record struct OverwriteTarget(MountOverwriteTargetType Type, long Id);
}

internal sealed class MountNameValidator : AbstractValidator<string>
{
    public MountNameValidator()
    {
        RuleFor(x => x)
            .Must(name => !string.IsNullOrWhiteSpace(name) && name.Trim().Length <= 200)
            .WithErrorCode(ErrorCodes.Mount.NameInvalid)
            .WithMessage("Mount name must contain 1 to 200 characters after trimming.");
    }
}

internal sealed class MountVisibilityValidator : AbstractValidator<MountVisibility>
{
    public MountVisibilityValidator()
    {
        RuleFor(x => x)
            .IsInEnum()
            .WithErrorCode(ErrorCodes.Mount.VisibilityInvalid)
            .WithMessage("Mount visibility must be a defined value.");
    }
}
