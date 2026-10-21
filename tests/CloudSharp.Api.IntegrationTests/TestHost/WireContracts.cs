namespace CloudSharp.Api.IntegrationTests.TestHost;

internal sealed record RegistrationWireRequest(
    string Email,
    string? UserName,
    string? DisplayName,
    string Password)
{
    public override string ToString() =>
        $"RegistrationWireRequest {{ Email = {Email}, UserName = {UserName}, "
        + $"DisplayName = {DisplayName}, Password = [redacted] }}";
}

internal sealed record LoginWireRequest(string LoginId, string Password)
{
    public override string ToString() =>
        $"LoginWireRequest {{ LoginId = {LoginId}, Password = [redacted] }}";
}

internal sealed record ChangePasswordWireRequest(string CurrentPassword, string NewPassword)
{
    public override string ToString() =>
        $"ChangePasswordWireRequest {{ CurrentPassword = [redacted], NewPassword = [redacted] }}";
}

internal sealed record AuthSessionWireResponse(
    string AccessToken,
    string TokenType,
    DateTimeOffset IdleExpiresAt,
    DateTimeOffset AbsoluteExpiresAt,
    UserWireResponse User)
{
    public override string ToString() =>
        $"AuthSessionWireResponse {{ AccessToken = [redacted], TokenType = {TokenType}, "
        + $"IdleExpiresAt = {IdleExpiresAt}, AbsoluteExpiresAt = {AbsoluteExpiresAt}, User = {User} }}";
}

internal sealed record UserWireResponse(
    Guid UserPublicId,
    string Email,
    string? UserName,
    string? DisplayName,
    string SystemRole,
    string Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

internal sealed record ErrorEnvelope(string RequestId, ErrorBodyWire Error);

internal sealed record ErrorBodyWire(
    string Code,
    string Message,
    IReadOnlyList<ErrorDetailWire> Details);

internal sealed record ErrorDetailWire(string? Field, string Code);
