namespace CloudSharp.Api.Contracts.ProblemDetails;

public sealed record ErrorBody(string Code, string Message, IReadOnlyList<ErrorDetail> Details);