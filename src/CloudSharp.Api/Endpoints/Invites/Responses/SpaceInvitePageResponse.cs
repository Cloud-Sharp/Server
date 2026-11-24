namespace CloudSharp.Api.Endpoints.Invites.Responses;

public sealed record SpaceInvitePageResponse(
    IReadOnlyList<SpaceInviteResponse> Items,
    string? NextCursor);
