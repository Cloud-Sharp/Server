using System.ComponentModel.DataAnnotations;

namespace CloudSharp.Api.Endpoints.Auth.Requests;

public sealed record RegistrationRequest(
    [property: Required, EmailAddress, StringLength(320)] string Email,
    [property: StringLength(30)] string? UserName,
    [property: StringLength(100)] string? DisplayName,
    [property: Required, StringLength(128, MinimumLength = 8)] string Password);
