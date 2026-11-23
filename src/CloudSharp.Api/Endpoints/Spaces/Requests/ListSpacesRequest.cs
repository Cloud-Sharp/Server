namespace CloudSharp.Api.Endpoints.Spaces.Requests;

public sealed record ListSpacesRequest(
    string? Cursor = null,
    int Limit = 50,
    string Sort = "createdAt",
    string Direction = "DESC");
