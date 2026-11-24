using System.Buffers.Text;
using System.Text.Json;
using CloudSharp.Core.Common.Tokens;

namespace CloudSharp.Core.UseCases.Invites.Listings;

internal static class SpaceInviteListCursor
{
    private const string SigningPrefix = "cloudsharp:space-invite-list:requester-cursor:v1:";

    internal static string Issue(string readerCursor, long requesterUserId, ITokenHasher tokenHasher)
    {
        var signature = tokenHasher.Hash(SigningInput(requesterUserId, readerCursor));
        var cursor = new SignedCursor(readerCursor, signature);
        return Base64Url.EncodeToString(JsonSerializer.SerializeToUtf8Bytes(cursor));
    }

    internal static bool TryOpen(string cursor, long requesterUserId, ITokenHasher tokenHasher, out string? readerCursor)
    {
        readerCursor = null;
        try
        {
            var bytes = Base64Url.DecodeFromChars(cursor);
            if (!string.Equals(Base64Url.EncodeToString(bytes), cursor, StringComparison.Ordinal))
            {
                return false;
            }

            var signed = JsonSerializer.Deserialize<SignedCursor>(bytes);
            if (signed is not { Cursor: { Length: > 0 } payload, Signature: { Length: > 0 } signature }
                || !bytes.AsSpan().SequenceEqual(JsonSerializer.SerializeToUtf8Bytes(signed))
                || !tokenHasher.Verify(SigningInput(requesterUserId, payload), signature))
            {
                return false;
            }

            readerCursor = payload;
            return true;
        }
        catch (Exception exception) when (exception is FormatException or JsonException)
        {
            return false;
        }
    }

    private static string SigningInput(long requesterUserId, string readerCursor) =>
        FormattableString.Invariant($"{SigningPrefix}{requesterUserId}:{readerCursor}");

    private sealed record SignedCursor(string Cursor, string Signature);
}
