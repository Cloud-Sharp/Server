using CloudSharp.Core.Abstractions.Auth;
using CloudSharp.Core.Common.Time;
using CloudSharp.Core.Common.Tokens;
using CloudSharp.Core.Domain.Sessions;
using CloudSharp.Core.Domain.Users;
using FluentResults;

namespace CloudSharp.Core.UseCases.Auth.Sessions;

/// <summary>
/// 로그인·회원가입의 트랜잭션 완료 후 토큰과 세션을 발급·저장한다.
/// 세션 저장에 성공한 경우에만 평문 토큰과 만료 시각을 반환한다.
/// </summary>
public sealed class SessionIssuanceService(
    ITokenIssuer tokenIssuer,
    ISessionStore sessionStore,
    IClock clock)
{
    public async Task<Result<SessionIssuanceResult>> IssueAsync(
        User user,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(user);

        if (user.Id <= 0)
        {
            throw new InvalidOperationException(
                "UserRepository returned a user without a positive internal id.");
        }

        cancellationToken.ThrowIfCancellationRequested();

        var issuedToken = tokenIssuer.Issue(TokenKind.Session);

        var tokenHashResult = TokenHash.Create(issuedToken.HashedToken);
        if (tokenHashResult.IsFailed)
        {
            return Result.Fail<SessionIssuanceResult>(tokenHashResult.Errors);
        }

        var session = UserSession.Issue(
            user.Id,
            user.PublicId,
            tokenHashResult.Value,
            user.SecurityVersion,
            clock.UtcNow);

        var storeResult = await sessionStore.StoreAsync(session, cancellationToken);
        if (storeResult.IsFailed)
        {
            return Result.Fail<SessionIssuanceResult>(storeResult.Errors);
        }

        return Result.Ok(new SessionIssuanceResult(
            issuedToken.PlainToken,
            session.IdleExpiresAt,
            session.AbsoluteExpiresAt));
    }
}
