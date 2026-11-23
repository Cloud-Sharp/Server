using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace CloudSharp.Api.Endpoints.Spaces.Requests;

public sealed record CreateSpaceRequest(
    [property: JsonRequired, Required] string Name,
    string? Description = null,
    [property: JsonNumberHandling(JsonNumberHandling.Strict)] long? AllowedBytes = null);
