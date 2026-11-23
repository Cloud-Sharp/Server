using System.Text.Json;

namespace CloudSharp.Api.Endpoints.Spaces.Requests;

public sealed record UpdateSpaceRequest
{
    public string? Name
    {
        get;
        init => field = value ?? throw new JsonException("The name field cannot be null.");
    }

    public string? Description
    {
        get;
        init
        {
            field = value;
            HasDescription = true;
        }
    }

    internal bool HasDescription { get; private set; }
}
