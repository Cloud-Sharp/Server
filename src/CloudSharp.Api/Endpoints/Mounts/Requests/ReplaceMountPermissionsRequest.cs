using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using CloudSharp.Core.Domain.Memberships;

namespace CloudSharp.Api.Endpoints.Mounts.Requests;

public sealed record ReplaceMountPermissionsRequest(
    [property: JsonRequired, Required] IReadOnlyList<SpacePermission> Allow,
    [property: JsonRequired, Required] IReadOnlyList<SpacePermission> Deny);
