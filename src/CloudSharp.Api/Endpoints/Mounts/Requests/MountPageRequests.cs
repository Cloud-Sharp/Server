using System.ComponentModel.DataAnnotations;

namespace CloudSharp.Api.Endpoints.Mounts.Requests;

public sealed record ListMountsRequest(
    string? Cursor = null,
    [property: Range(1, 100)] int Limit = 50,
    [property: Required, RegularExpression(@"\ACREATED_AT\z")] string Sort = "CREATED_AT",
    [property: Required, RegularExpression(@"\A(ASC|DESC)\z")] string Direction = "DESC");

public sealed record ListMountPermissionsRequest(
    string? TargetType = null,
    string? Cursor = null,
    [property: Range(1, 100)] int Limit = 50);

public sealed record PreviewMountPermissionsRequest(
    string? UserPublicId = null,
    string? Visibility = null,
    string? Cursor = null,
    [property: Range(1, 100)] int Limit = 50);
