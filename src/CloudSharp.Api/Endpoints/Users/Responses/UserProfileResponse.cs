namespace CloudSharp.Api.Endpoints.Users.Responses;

internal sealed record UserProfileResponse(
    Guid UserPublicId,
    string Email,
    string? UserName,
    string? DisplayName,
    string SystemRole,
    string Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);
