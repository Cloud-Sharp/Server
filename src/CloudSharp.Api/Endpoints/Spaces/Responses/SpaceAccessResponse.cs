namespace CloudSharp.Api.Endpoints.Spaces.Responses;

internal sealed record SpaceAccessResponse(
    string Source,
    IReadOnlyList<string> Permissions);
