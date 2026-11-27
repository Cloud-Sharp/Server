using System.Collections.Frozen;
using CloudSharp.Core.Common.Errors;
using CloudSharp.Core.Common.Results;
using CloudSharp.Core.Domain.Memberships;
using FluentResults;
using FluentValidation;

namespace CloudSharp.Core.Domain.Mounts;

/// <summary>
/// Mount 하나에서 같은 Space의 Role 또는 사용자에게 적용하는 권한 덮어쓰기다. <see cref="Allow"/>는 Space
/// 기본 권한에 더하고 <see cref="Deny"/>는 뺀다. 두 집합은 Mount 범위 권한만 담으며 서로 겹치지 않는다.
/// </summary>
public sealed class MountPermissionOverwrite
{
    public MountOverwriteTargetType TargetType { get; private set; }
    public long TargetId { get; private set; }
    public IReadOnlySet<SpacePermission> Allow { get; private set; } = FrozenSet<SpacePermission>.Empty;
    public IReadOnlySet<SpacePermission> Deny { get; private set; } = FrozenSet<SpacePermission>.Empty;

    /// <summary>허용과 거부가 모두 비어 있으면 저장할 덮어쓰기가 없다.</summary>
    public bool IsEmpty => Allow.Count == 0 && Deny.Count == 0;

    private MountPermissionOverwrite() { }

    public static Result<MountPermissionOverwrite> Create(
        MountOverwriteTargetType targetType,
        long targetId,
        IEnumerable<SpacePermission> allow,
        IEnumerable<SpacePermission> deny) =>
        CreateCore(targetType, targetId, allow, deny, allowEmpty: false);

    public static MountPermissionOverwrite Reconstitute(
        MountOverwriteTargetType targetType,
        long targetId,
        IEnumerable<SpacePermission> allow,
        IEnumerable<SpacePermission> deny)
    {
        if (!Enum.IsDefined(targetType))
        {
            throw new ArgumentOutOfRangeException(nameof(targetType), targetType, "Unsupported mount overwrite target type.");
        }

        if (targetId <= 0)
        {
            throw new ArgumentException("Mount overwrite target id must be positive.", nameof(targetId));
        }

        ArgumentNullException.ThrowIfNull(allow);
        ArgumentNullException.ThrowIfNull(deny);
        var overwrite = FromValidated(targetType, targetId, allow, deny);
        if (overwrite.IsEmpty
            || !overwrite.Allow.All(MountScopedPermissions.Contains)
            || !overwrite.Deny.All(MountScopedPermissions.Contains)
            || overwrite.Allow.Overlaps(overwrite.Deny))
        {
            throw new ArgumentException(
                "Mount overwrite must be nonempty, mount-scoped and must not allow and deny the same permission.",
                nameof(allow));
        }

        return overwrite;
    }

    /// <summary>
    /// 이 덮어쓰기를 <paramref name="next"/>로 바꿀 때 허용·거부 상태가 달라지는 권한이다.
    /// <paramref name="current"/>가 없으면 <paramref name="next"/>의 모든 권한이 바뀐다.
    /// </summary>
    public static IReadOnlySet<SpacePermission> ChangedPermissions(
        MountPermissionOverwrite? current,
        MountPermissionOverwrite next)
    {
        ArgumentNullException.ThrowIfNull(next);
        var changed = new HashSet<SpacePermission>(next.Allow);
        changed.SymmetricExceptWith(current?.Allow ?? FrozenSet<SpacePermission>.Empty);
        var deniedChanges = new HashSet<SpacePermission>(next.Deny);
        deniedChanges.SymmetricExceptWith(current?.Deny ?? FrozenSet<SpacePermission>.Empty);
        changed.UnionWith(deniedChanges);
        return changed.ToFrozenSet();
    }

    internal static Result<MountPermissionOverwrite> CreateForMutation(
        MountOverwriteTargetType targetType,
        long targetId,
        IEnumerable<SpacePermission>? allow,
        IEnumerable<SpacePermission>? deny) =>
        CreateCore(targetType, targetId, allow, deny, allowEmpty: true);

    private static MountPermissionOverwrite FromValidated(
        MountOverwriteTargetType targetType,
        long targetId,
        IEnumerable<SpacePermission> allow,
        IEnumerable<SpacePermission> deny) =>
        new()
        {
            TargetType = targetType,
            TargetId = targetId,
            Allow = allow.ToFrozenSet(),
            Deny = deny.ToFrozenSet(),
        };

    private static Result<MountPermissionOverwrite> CreateCore(
        MountOverwriteTargetType targetType,
        long targetId,
        IEnumerable<SpacePermission>? allow,
        IEnumerable<SpacePermission>? deny,
        bool allowEmpty)
    {
        if (allow is null || deny is null)
        {
            return Result.Fail<MountPermissionOverwrite>(MountError.PermissionsInvalid());
        }

        var candidate = FromValidated(targetType, targetId, allow, deny);
        var validation = new MountPermissionOverwriteValidator(allowEmpty).Validate(candidate);
        return validation.IsValid ? Result.Ok(candidate) : validation.ToFailureResult<MountPermissionOverwrite>();
    }
}

internal sealed class MountPermissionOverwriteValidator : AbstractValidator<MountPermissionOverwrite>
{
    public MountPermissionOverwriteValidator(bool allowEmpty)
    {
        RuleFor(x => x.TargetType)
            .IsInEnum()
            .WithErrorCode(ErrorCodes.Mount.TargetTypeInvalid)
            .WithMessage("Mount overwrite target type must be a defined value.");

        RuleFor(x => x.TargetId)
            .GreaterThan(0)
            .WithErrorCode(ErrorCodes.Mount.TargetIdInvalid)
            .WithMessage("Mount overwrite target id must be positive.");

        RuleFor(x => x)
            .Must(overwrite => (allowEmpty || !overwrite.IsEmpty)
                && overwrite.Allow.All(MountScopedPermissions.Contains)
                && overwrite.Deny.All(MountScopedPermissions.Contains)
                && !overwrite.Allow.Overlaps(overwrite.Deny))
            .WithErrorCode(ErrorCodes.Mount.PermissionsInvalid)
            .WithMessage(
                "Mount overwrite must contain only mount-scoped permissions, must not allow and deny the same permission, "
                + "and must satisfy the operation's nonempty rule.")
            .OverridePropertyName("Permissions");
    }
}
