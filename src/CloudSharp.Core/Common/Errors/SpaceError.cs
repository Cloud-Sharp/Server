namespace CloudSharp.Core.Common.Errors;

/// <summary>
/// Space의 상태·소유권·용량 불변 조건에 대한 비즈니스 오류.
/// </summary>
public sealed class SpaceError : CloudSharpError
{
    private SpaceError(string code, string message) : base(new ErrorDefinition(code), message) { }

    public static SpaceError NameInvalid() =>
        new(ErrorCodes.Space.NameInvalid, "Space name is required and must not exceed 200 characters.");

    public static SpaceError OwnerInvalid() =>
        new(ErrorCodes.Space.OwnerInvalid, "Space owner id must be positive.");

    public static SpaceError Inactive() =>
        new(ErrorCodes.Space.Inactive, "Space is not available for this operation.");

    public static SpaceError InvalidState(string message) =>
        new(ErrorCodes.Space.InvalidState, message);

    public static SpaceError QuotaExceeded() =>
        new(ErrorCodes.Space.QuotaExceeded, "The reservation exceeds the available space capacity.");

    public static SpaceError QuotaTooSmall() =>
        new(ErrorCodes.Space.QuotaTooSmall, "Quota cannot be smaller than used and reserved storage.");

    public static SpaceError StorageAmountInvalid() =>
        new(ErrorCodes.Space.StorageAmountInvalid, "Storage amount must be positive and within the available balance.");

    public static SpaceError ReservationInvalid() =>
        new(ErrorCodes.Space.ReservationInvalid, "Storage amount exceeds the current reservation balance.");

    public static SpaceError TransferTargetInvalid() =>
        new(ErrorCodes.Space.TransferTargetInvalid, "New owner id must be positive.");
}
