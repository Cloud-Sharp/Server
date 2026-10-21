using System.ComponentModel.DataAnnotations;

namespace CloudSharp.Api.Endpoints.Users.Requests;

public sealed record ChangePasswordRequest(
    [property: Required, StringLength(128)] string CurrentPassword,
    [property: Required, StringLength(128, MinimumLength = 8)] string NewPassword);
