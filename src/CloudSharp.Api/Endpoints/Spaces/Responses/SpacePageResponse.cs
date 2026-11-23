namespace CloudSharp.Api.Endpoints.Spaces.Responses;

internal sealed record SpacePageResponse(
    IReadOnlyList<SpaceResponse> Items,
    string? NextCursor);
