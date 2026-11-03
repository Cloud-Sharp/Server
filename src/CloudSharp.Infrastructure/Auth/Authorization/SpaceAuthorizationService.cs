using CloudSharp.Core.Abstractions.Auth;
using CloudSharp.Core.Abstractions.Persistence;
using CloudSharp.Core.Policies.Authorization;
using FluentResults;

namespace CloudSharp.Infrastructure.Auth.Authorization;

/// <summary>조회 port의 최신 snapshot을 Core Session 인가 정책에 전달한다. DB·HTTP·감사 저장을 직접 다루지 않는다.</summary>
public sealed class SpaceAuthorizationService : ISpaceAuthorizationService
{
    private readonly ISpaceAuthorizationReader _reader;

    public SpaceAuthorizationService(ISpaceAuthorizationReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);
        _reader = reader;
    }

    public async Task<Result<AuthorizedSpaceContext>> AuthorizeAsync(
        SpaceAuthorizationQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        cancellationToken.ThrowIfCancellationRequested();

        var actorValidation = SessionSpaceAuthorizationPolicy.ValidateActor(query.Actor);
        if (actorValidation.IsFailed)
        {
            return Result.Fail<AuthorizedSpaceContext>(actorValidation.Errors);
        }

        var read = await _reader.ReadAsync(query.SpacePublicId, query.Actor.UserId, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        if (read.IsFailed)
        {
            return Result.Fail<AuthorizedSpaceContext>(read.Errors);
        }

        return SessionSpaceAuthorizationPolicy.Evaluate(query, read.Value);
    }
}
