using System.Text.Json;
using CloudSharp.Core.Common.Errors;
using CloudSharp.Core.Common.Tokens;
using CloudSharp.Infrastructure.Auth.Tokens;
using FluentResults;

namespace CloudSharp.Infrastructure.Persistence.Repositories;

internal static class MountReadCursor
{
    public static string Issue(ITokenHasher hasher, string scope, Guid publicId, long? createdAtTicks = null)
    {
        var payload = Base64Url.Encode(JsonSerializer.SerializeToUtf8Bytes(new Position(publicId, createdAtTicks)));
        return $"{payload}.{hasher.Hash(SigningInput(scope, payload))}";
    }

    public static bool TryRead(ITokenHasher hasher, string scope, string cursor, out Position position)
    {
        position = default;
        var separator = cursor.IndexOf('.');
        if (separator <= 0 || separator == cursor.Length - 1)
        {
            return false;
        }

        var payload = cursor[..separator];
        var signature = cursor[(separator + 1)..];
        try
        {
            if (!string.Equals(Base64Url.Encode(Base64Url.Decode(signature)), signature, StringComparison.Ordinal)
                || !hasher.Verify(SigningInput(scope, payload), signature))
            {
                return false;
            }

            var bytes = Base64Url.Decode(payload);
            if (!string.Equals(Base64Url.Encode(bytes), payload, StringComparison.Ordinal))
            {
                return false;
            }

            position = JsonSerializer.Deserialize<Position>(bytes);
            return position.PublicId != Guid.Empty;
        }
        catch (Exception error) when (error is FormatException or JsonException)
        {
            return false;
        }
    }

    public static Error Invalid() =>
        new Error("The cursor is invalid.").WithMetadata(CloudSharpError.ErrorCodeMetadataKey, ErrorCodes.Common.CursorInvalid);

    private static string SigningInput(string scope, string payload) => $"cloudsharp:mount-cursor:v1:{scope}.{payload}";

    public readonly record struct Position(Guid PublicId, long? CreatedAtTicks);
}
