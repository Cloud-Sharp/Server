using CloudSharp.Core.Common.Actors;

namespace CloudSharp.Api.Auth.CurrentUser;

internal sealed class CurrentActorAccessor : ICurrentActorAccessor
{
    public CurrentActor? CurrentActor { get; private set; }

    public string? SessionTokenHash { get; private set; }

    public void Set(CurrentActor actor, string sessionTokenHash)
    {
        ArgumentNullException.ThrowIfNull(actor);
        ArgumentNullException.ThrowIfNull(sessionTokenHash);

        CurrentActor = actor;
        SessionTokenHash = sessionTokenHash;
    }
}
