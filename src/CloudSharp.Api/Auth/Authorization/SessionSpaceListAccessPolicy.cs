using CloudSharp.Core.Abstractions.Auth;
using CloudSharp.Core.Common.Actors;
using CloudSharp.Core.Domain.Spaces;
using CloudSharp.Core.Policies.Authorization;

namespace CloudSharp.Api.Auth.Authorization;

internal sealed class SessionSpaceListAccessPolicy : ISpaceListAccessPolicy
{
    private static readonly SpaceStatus[] AllowedStatuses = [SpaceStatus.Active, SpaceStatus.Archived];

    public AuthorizedSpaceContext? Evaluate(CurrentActor actor, SpaceAuthorizationSnapshot snapshot)
    {
        var query = new SpaceAuthorizationQuery(actor, snapshot.Space.PublicId, [], AllowedStatuses);
        var result = SessionSpaceAuthorizationPolicy.Evaluate(query, snapshot);
        return result.IsSuccess ? result.Value : null;
    }
}
