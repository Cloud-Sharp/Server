namespace CloudSharp.Core.Common.Errors;

public sealed class MountError : CloudSharpError
{
    private MountError(string code, string message) : base(new ErrorDefinition(code), message) { }

    public static MountError SpaceIdInvalid() =>
        new(ErrorCodes.Mount.SpaceIdInvalid, "Mount space id must be positive.");

    public static MountError NameInvalid() =>
        new(ErrorCodes.Mount.NameInvalid, "Mount name must contain 1 to 200 characters after trimming.");

    public static MountError VisibilityInvalid() =>
        new(ErrorCodes.Mount.VisibilityInvalid, "Mount visibility must be a defined value.");

    public static MountError TargetTypeInvalid() =>
        new(ErrorCodes.Mount.TargetTypeInvalid, "Mount grant target type must be a defined value.");

    public static MountError TargetIdInvalid() =>
        new(ErrorCodes.Mount.TargetIdInvalid, "Mount grant target id must be positive.");

    public static MountError PermissionsInvalid() =>
        new(ErrorCodes.Mount.PermissionsInvalid, "Mount permissions must contain only defined values and satisfy the operation's nonempty rule.");

    public static MountError GrantsInvalid(string message) =>
        new(ErrorCodes.Mount.GrantsInvalid, message);

    public static MountError SettingsRequired() =>
        new(ErrorCodes.Mount.SettingsRequired, "At least one mount setting must be provided.");

    public static MountError InvalidState(string message) =>
        new(ErrorCodes.Mount.InvalidState, message);
}
