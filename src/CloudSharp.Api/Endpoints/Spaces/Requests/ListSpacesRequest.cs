using System.ComponentModel.DataAnnotations;

namespace CloudSharp.Api.Endpoints.Spaces.Requests;

public sealed record ListSpacesRequest(
    string? Cursor = null,
    [property: Range(1, 100)] int Limit = 50,
    [property: RegularExpression("^createdAt$")] string Sort = "createdAt",
    [property: RegularExpression("^(ASC|DESC)$")] string Direction = "DESC");
