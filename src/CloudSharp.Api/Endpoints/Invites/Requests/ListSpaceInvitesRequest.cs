using System.ComponentModel.DataAnnotations;

namespace CloudSharp.Api.Endpoints.Invites.Requests;

public sealed record ListSpaceInvitesRequest(
    string? Cursor = null,
    [property: Range(1, 100)] int Limit = 50,
    string? Sort = null,
    [property: Required, RegularExpression("^(ASC|DESC)$")] string Direction = "DESC");
