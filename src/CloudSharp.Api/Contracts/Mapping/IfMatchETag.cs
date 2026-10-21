using System.Globalization;
using Microsoft.AspNetCore.Http;

namespace CloudSharp.Api.Contracts.Mapping;

internal static class IfMatchETag
{
    private const string HeaderName = "If-Match";
    private const string InvalidDetailCode = "INVALID_ETAG";

    internal static bool TryRead(HttpRequest request, out long version, out IResult? error)
    {
        version = 0;
        var values = request.Headers[HeaderName];
        if (values.Count == 0)
        {
            error = ErrorHttpResults.FromCode(
                Core.Common.Errors.ErrorCodes.Common.PreconditionRequired,
                "An If-Match header is required.");
            return false;
        }

        if (values.Count != 1 || !TryParse(values[0], out version))
        {
            error = ErrorHttpResults.Validation(
                "ifMatch",
                InvalidDetailCode,
                "If-Match must contain a single strong ETag in the form \"v{version}\".");
            return false;
        }

        error = null;
        return true;
    }

    private static bool TryParse(string? rawValue, out long version)
    {
        version = 0;
        if (string.IsNullOrWhiteSpace(rawValue))
        {
            return false;
        }

        var value = rawValue.Trim();
        if (value.Length < 4
            || value[0] != '"'
            || value[^1] != '"'
            || value.StartsWith("W/", StringComparison.Ordinal))
        {
            return false;
        }

        var inner = value[1..^1];
        if (inner.Length < 2
            || inner[0] != 'v'
            || inner.Contains(','))
        {
            return false;
        }

        return long.TryParse(
            inner[1..],
            NumberStyles.None,
            CultureInfo.InvariantCulture,
            out version)
            && version > 0;
    }
}
