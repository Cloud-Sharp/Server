namespace CloudSharp.Api.Endpoints.Auth.Responses;

internal sealed record AuthSessionResponse(
    string AccessToken,
    string TokenType,
    DateTimeOffset IdleExpiresAt,
    DateTimeOffset AbsoluteExpiresAt,
    UserResponse User)
{
    public override string ToString() =>
        $"AuthSessionResponse {{ AccessToken = [redacted], TokenType = {TokenType}, "
        + $"IdleExpiresAt = {IdleExpiresAt}, AbsoluteExpiresAt = {AbsoluteExpiresAt}, User = {User} }}";
}
