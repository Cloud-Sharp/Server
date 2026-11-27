using System.Text.Json;
using System.Text.Json.Serialization;
using CloudSharp.Core.Common.Errors;
using CloudSharp.Infrastructure.Auth.Tokens;
using FluentResults;

namespace CloudSharp.Infrastructure.Persistence.Repositories;

/// <summary>
/// keyset 목록에서 다음 페이지가 시작할 위치를 담는 저장 계층 위치 토큰이다. 정렬값 ticks와 tie-breaker
/// PublicId만 담고 내부 long ID는 담지 않는다. 요청자·조회 조건 묶음과 서명은 API의 page cursor가 맡으므로
/// 이 토큰은 형식이 정확한지만 확인한다.
/// </summary>
internal static class KeysetCursor
{
    private const int CurrentVersion = 1;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static string Issue(Guid publicId, DateTimeOffset? sortValue = null) =>
        Base64Url.Encode(JsonSerializer.SerializeToUtf8Bytes(
            new Payload(CurrentVersion, publicId, sortValue?.UtcTicks),
            JsonOptions));

    /// <summary>
    /// 정규 base64url JSON이고 버전·PublicId·정렬값 범위가 올바를 때만 위치를 반환한다. 정렬값이 있어야 하는지는
    /// 호출한 reader가 <see cref="Position.SortValue"/>로 확인한다.
    /// </summary>
    public static bool TryRead(string cursor, out Position position)
    {
        position = default;
        try
        {
            var bytes = Base64Url.Decode(cursor);
            if (!string.Equals(Base64Url.Encode(bytes), cursor, StringComparison.Ordinal))
            {
                return false;
            }

            var payload = JsonSerializer.Deserialize<Payload>(bytes, JsonOptions);
            if (payload is null
                || payload.Version != CurrentVersion
                || payload.PublicId == Guid.Empty
                || payload.SortTicks is < 0
                || payload.SortTicks > DateTime.MaxValue.Ticks
                || !bytes.AsSpan().SequenceEqual(JsonSerializer.SerializeToUtf8Bytes(payload, JsonOptions)))
            {
                return false;
            }

            position = new Position(
                payload.PublicId,
                payload.SortTicks is { } ticks ? new DateTimeOffset(ticks, TimeSpan.Zero) : null);
            return true;
        }
        catch (Exception exception) when (exception is FormatException or JsonException)
        {
            return false;
        }
    }

    public static Error Invalid() =>
        new Error("The cursor is invalid.")
            .WithMetadata(CloudSharpError.ErrorCodeMetadataKey, ErrorCodes.Common.CursorInvalid);

    public readonly record struct Position(Guid PublicId, DateTimeOffset? SortValue);

    private sealed record Payload(
        [property: JsonPropertyName("v")] int Version,
        [property: JsonPropertyName("id")] Guid PublicId,
        [property: JsonPropertyName("t")] long? SortTicks);
}
