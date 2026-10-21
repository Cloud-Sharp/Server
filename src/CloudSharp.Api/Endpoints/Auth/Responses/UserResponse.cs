namespace CloudSharp.Api.Endpoints.Auth.Responses;

internal sealed record UserResponse(
    Guid UserPublicId,
    string Email,
    string? UserName,
    string? DisplayName,
    string SystemRole,
    string Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);
