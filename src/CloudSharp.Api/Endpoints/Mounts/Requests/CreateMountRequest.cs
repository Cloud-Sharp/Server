using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using CloudSharp.Core.Domain.Memberships;
using CloudSharp.Core.Domain.Mounts;

namespace CloudSharp.Api.Endpoints.Mounts.Requests;

public sealed record CreateMountRequest(
    [property: JsonRequired, Required] string Name,
    [property: JsonRequired, Required] MountVisibility? Visibility,
    IReadOnlyList<InitialRoleOverwriteRequest?>? RoleOverwrites = null,
    IReadOnlyList<InitialUserOverwriteRequest?>? UserOverwrites = null);

public sealed record InitialRoleOverwriteRequest(
    [property: JsonRequired] Guid RolePublicId,
    [property: JsonRequired, Required] IReadOnlyList<SpacePermission> Allow,
    [property: JsonRequired, Required] IReadOnlyList<SpacePermission> Deny);

public sealed record InitialUserOverwriteRequest(
    [property: JsonRequired] Guid UserPublicId,
    [property: JsonRequired, Required] IReadOnlyList<SpacePermission> Allow,
    [property: JsonRequired, Required] IReadOnlyList<SpacePermission> Deny);
