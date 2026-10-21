namespace CloudSharp.Core.UseCases.Auth.Authentication;

/// <summary>
/// Session authentication input. <see cref="TokenHash"/> is an HMAC digest and never a raw token.
/// ToString is redacted to keep the hash out of logs.
/// </summary>
public sealed record AuthenticateSessionQuery(string TokenHash)
{
    public override string ToString() => "AuthenticateSessionQuery { TokenHash = [redacted] }";
}
