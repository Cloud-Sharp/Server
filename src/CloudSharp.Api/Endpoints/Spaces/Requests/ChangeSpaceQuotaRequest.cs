using System.Text.Json.Serialization;

namespace CloudSharp.Api.Endpoints.Spaces.Requests;

public sealed record ChangeSpaceQuotaRequest(
    [property: JsonRequired, JsonNumberHandling(JsonNumberHandling.Strict)] long? AllowedBytes);
