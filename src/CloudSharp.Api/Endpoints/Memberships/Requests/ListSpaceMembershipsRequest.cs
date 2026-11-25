using System.ComponentModel.DataAnnotations;

namespace CloudSharp.Api.Endpoints.Memberships.Requests;

public sealed record ListSpaceMembershipsRequest(
    string? Cursor = null,
    [property: Range(1, 100)] int Limit = 50,
    string Sort = "joinedAt",
    [property: Required, RegularExpression("^(ASC|DESC)$")] string Direction = "DESC");
