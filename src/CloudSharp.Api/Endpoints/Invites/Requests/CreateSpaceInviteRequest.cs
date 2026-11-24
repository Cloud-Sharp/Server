using System.Text.Json.Serialization;

namespace CloudSharp.Api.Endpoints.Invites.Requests;

public sealed record CreateSpaceInviteRequest(
    [property: JsonRequired] DateTimeOffset ExpiresAt,
    [property: JsonNumberHandling(JsonNumberHandling.Strict)] int? MaxUses = null);
