using System.Text.Json.Serialization;

namespace CloudSharp.Api.Endpoints.Spaces.Requests;

public sealed record TransferSpaceOwnershipRequest(
    [property: JsonRequired] Guid NewOwnerUserPublicId);
