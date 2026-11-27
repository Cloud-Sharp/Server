using CloudSharp.Core.Abstractions.Auth;
using CloudSharp.Core.Abstractions.Persistence;
using CloudSharp.Core.Domain.Mounts;
using CloudSharp.Core.Policies.Authorization;
using FluentResults;

namespace CloudSharp.Infrastructure.Auth.Authorization;

/// <summary>
/// 조회 port의 최신 Space snapshot과 Mount를 Core Mount 인가 정책에 전달한다.
/// DB·HTTP·감사 저장을 직접 다루지 않는다.
/// </summary>
public sealed class MountAuthorizationService : IMountAuthorizationService
{
    private readonly ISpaceAuthorizationReader _spaceReader;
    private readonly IMountReader _mountReader;

    public MountAuthorizationService(ISpaceAuthorizationReader spaceReader, IMountReader mountReader)
    {
        ArgumentNullException.ThrowIfNull(spaceReader);
        ArgumentNullException.ThrowIfNull(mountReader);
        _spaceReader = spaceReader;
        _mountReader = mountReader;
    }

    public async Task<Result<AuthorizedMountContext>> AuthorizeAsync(
        MountAuthorizationQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        cancellationToken.ThrowIfCancellationRequested();

        var actorValidation = SessionSpaceAuthorizationPolicy.ValidateActor(query.Actor);
        if (actorValidation.IsFailed)
        {
            return Result.Fail<AuthorizedMountContext>(actorValidation.Errors);
        }

        var spaceRead = await _spaceReader.ReadAsync(query.SpacePublicId, query.Actor.UserId, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        if (spaceRead.IsFailed)
        {
            return Result.Fail<AuthorizedMountContext>(spaceRead.Errors);
        }

        Mount? mount = null;
        if (spaceRead.Value is { } snapshot)
        {
            var mountRead = await _mountReader.ReadAsync(snapshot.Space.Id, query.MountPublicId, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (mountRead.IsFailed)
            {
                return Result.Fail<AuthorizedMountContext>(mountRead.Errors);
            }

            mount = mountRead.Value;
        }

        return MountAuthorizationPolicy.Evaluate(query, spaceRead.Value, mount);
    }
}
