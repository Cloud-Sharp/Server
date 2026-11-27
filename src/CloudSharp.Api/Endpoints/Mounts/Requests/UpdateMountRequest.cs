using CloudSharp.Core.Domain.Mounts;

namespace CloudSharp.Api.Endpoints.Mounts.Requests;

public sealed record UpdateMountRequest
{
    public string? Name
    {
        get;
        init
        {
            field = value;
            HasName = true;
        }
    }

    public MountVisibility? Visibility
    {
        get;
        init
        {
            field = value;
            HasVisibility = true;
        }
    }

    internal bool HasName { get; private set; }
    internal bool HasVisibility { get; private set; }
}
