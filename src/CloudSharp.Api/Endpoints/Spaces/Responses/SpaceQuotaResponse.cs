namespace CloudSharp.Api.Endpoints.Spaces.Responses;

internal sealed record SpaceQuotaResponse(
    Guid SpacePublicId,
    long? AllowedBytes,
    long UsedBytes,
    long ReservedBytes,
    long? AvailableBytes);
