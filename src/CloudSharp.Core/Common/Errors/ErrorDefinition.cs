using System.Net;

namespace CloudSharp.Core.Common.Errors;

public sealed record ErrorDefinition(string Code, HttpStatusCode StatusCode);
