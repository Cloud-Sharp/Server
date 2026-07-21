namespace CloudSharp.Api.Contracts.ProblemDetails;

public sealed record ErrorResponse(string RequestId, ErrorBody Error);