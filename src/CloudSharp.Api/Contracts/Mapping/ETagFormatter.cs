using System.Globalization;

namespace CloudSharp.Api.Contracts.Mapping;

internal static class ETagFormatter
{
    internal static string Format(long version) =>
        $"\"v{version.ToString(CultureInfo.InvariantCulture)}\"";
}
