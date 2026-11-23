namespace CloudSharp.Api.Contracts.Mapping;

internal static class PublicIdParser
{
    internal static bool TryParse(string? value, out Guid publicId) =>
        Guid.TryParseExact(value, "D", out publicId)
        && publicId.Version == 7
        && publicId.Variant is >= 8 and <= 11;
}
