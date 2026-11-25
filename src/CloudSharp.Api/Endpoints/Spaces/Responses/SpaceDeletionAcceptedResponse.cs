namespace CloudSharp.Api.Endpoints.Spaces.Responses;

internal sealed record SpaceDeletionAcceptedResponse(
    Guid SpacePublicId,
    string Status,
    DateTimeOffset AcceptedAt);
