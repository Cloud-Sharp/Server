using System.Collections.Frozen;
using CloudSharp.Core.Common.Errors;
using CloudSharp.Core.Common.Results;
using FluentResults;
using FluentValidation;

namespace CloudSharp.Core.Domain.Mounts;

public sealed class MountAccessGrant
{
    public MountGrantTargetType TargetType { get; private set; }
    public long TargetId { get; private set; }
    public IReadOnlySet<MountPermission> Permissions { get; private set; } = FrozenSet<MountPermission>.Empty;

    private MountAccessGrant() { }

    public static Result<MountAccessGrant> Create(
        MountGrantTargetType targetType,
        long targetId,
        IEnumerable<MountPermission> permissions) =>
        CreateCore(targetType, targetId, permissions, allowEmpty: false);

    public static MountAccessGrant Reconstitute(
        MountGrantTargetType targetType,
        long targetId,
        IEnumerable<MountPermission> permissions)
    {
        if (!Enum.IsDefined(targetType))
        {
            throw new ArgumentOutOfRangeException(nameof(targetType), targetType, "Unsupported mount grant target type.");
        }

        if (targetId <= 0)
        {
            throw new ArgumentException("Mount grant target id must be positive.", nameof(targetId));
        }

        ArgumentNullException.ThrowIfNull(permissions);
        var grant = FromValidated(targetType, targetId, permissions);
        if (grant.Permissions.Count == 0 || grant.Permissions.Any(permission => !Enum.IsDefined(permission)))
        {
            throw new ArgumentException("Mount grant permissions must be nonempty and contain only defined values.", nameof(permissions));
        }

        return grant;
    }

    internal static Result<MountAccessGrant> CreateForMutation(
        MountGrantTargetType targetType,
        long targetId,
        IEnumerable<MountPermission> permissions) =>
        CreateCore(targetType, targetId, permissions, allowEmpty: true);

    internal static MountAccessGrant FromValidated(
        MountGrantTargetType targetType,
        long targetId,
        IEnumerable<MountPermission> permissions) =>
        new()
        {
            TargetType = targetType,
            TargetId = targetId,
            Permissions = permissions.ToFrozenSet(),
        };

    private static Result<MountAccessGrant> CreateCore(
        MountGrantTargetType targetType,
        long targetId,
        IEnumerable<MountPermission>? permissions,
        bool allowEmpty)
    {
        if (permissions is null)
        {
            return Result.Fail<MountAccessGrant>(MountError.PermissionsInvalid());
        }

        var candidate = new MountAccessGrant
        {
            TargetType = targetType,
            TargetId = targetId,
            Permissions = permissions.ToFrozenSet(),
        };
        var validation = new MountAccessGrantValidator(allowEmpty).Validate(candidate);
        return validation.IsValid ? Result.Ok(candidate) : validation.ToFailureResult<MountAccessGrant>();
    }
}

internal sealed class MountAccessGrantValidator : AbstractValidator<MountAccessGrant>
{
    public MountAccessGrantValidator(bool allowEmpty)
    {
        RuleFor(x => x.TargetType)
            .IsInEnum()
            .WithErrorCode(ErrorCodes.Mount.TargetTypeInvalid)
            .WithMessage("Mount grant target type must be a defined value.");

        RuleFor(x => x.TargetId)
            .GreaterThan(0)
            .WithErrorCode(ErrorCodes.Mount.TargetIdInvalid)
            .WithMessage("Mount grant target id must be positive.");

        RuleFor(x => x.Permissions)
            .Must(permissions =>
                (allowEmpty || permissions.Count > 0)
                && permissions.All(permission => Enum.IsDefined(permission)))
            .WithErrorCode(ErrorCodes.Mount.PermissionsInvalid)
            .WithMessage("Mount permissions must contain only defined values and satisfy the operation's nonempty rule.");
    }
}
