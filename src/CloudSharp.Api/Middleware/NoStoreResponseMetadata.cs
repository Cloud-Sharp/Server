namespace CloudSharp.Api.Middleware;

/// <summary>Marks endpoints whose success and failure responses must never be cached.</summary>
internal sealed class NoStoreResponseMetadata
{
    internal static readonly NoStoreResponseMetadata Instance = new();

    private NoStoreResponseMetadata() { }
}
