using System.Buffers.Text;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using CloudSharp.Api.Auth.CurrentUser;
using CloudSharp.Api.Contracts.Mapping;
using CloudSharp.Core.Common.Tokens;

namespace CloudSharp.Api.Contracts.Paging;

/// <summary>
/// 목록 응답의 <c>nextCursor</c>를 HTTP 요청에 묶어 서명하고, 다음 요청에서 검증한다.
/// 저장 계층이 발급한 keyset 위치 토큰을 감싸며, 묶음은 요청자, 메서드, route 패턴과 값,
/// <c>cursor</c>·<c>limit</c>을 뺀 요청 DTO다. 엔드포인트에 조회 조건을 추가하면 묶음도 자동으로 따라가므로
/// cursor 범위 때문에 Core·Infrastructure를 바꾸지 않는다.
/// </summary>
/// <remarks>
/// 목록 handler는 요청의 cursor를 <see cref="TryOpen"/>으로 풀어 Query에 넘기고, 결과의 다음 위치를
/// <see cref="Seal"/>로 감싸 응답한다. 같은 요청 DTO 인스턴스를 두 호출에 사용한다.
/// </remarks>
internal static class PageCursor
{
    private const string SigningPrefix = "cloudsharp:page-cursor:v1:";
    private static readonly string[] UnboundRequestProperties = ["Cursor", "Limit"];

    /// <summary>
    /// 요청의 cursor를 검증하고 저장 계층 위치 토큰을 꺼낸다. cursor가 없으면 위치 없이 성공한다.
    /// 형식이 틀렸거나 다른 요청자·경로·조회 조건에서 발급된 cursor면 <c>false</c>다.
    /// </summary>
    internal static bool TryOpen(HttpContext context, object request, string? cursor, out string? position)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(request);
        position = null;
        if (cursor is null)
        {
            return true;
        }

        try
        {
            var bytes = Base64Url.DecodeFromChars(cursor);
            if (!string.Equals(Base64Url.EncodeToString(bytes), cursor, StringComparison.Ordinal))
            {
                return false;
            }

            var envelope = JsonSerializer.Deserialize<Envelope>(bytes);
            if (envelope is not { Position: { Length: > 0 } inner, Signature: { Length: > 0 } signature }
                || !bytes.AsSpan().SequenceEqual(JsonSerializer.SerializeToUtf8Bytes(envelope))
                || !Hasher(context).Verify(SigningInput(context, request, inner), signature))
            {
                return false;
            }

            position = inner;
            return true;
        }
        catch (Exception exception) when (exception is FormatException or JsonException)
        {
            return false;
        }
    }

    /// <summary>저장 계층 위치 토큰을 현재 요청에 묶어 응답용 cursor로 만든다. 다음 위치가 없으면 <c>null</c>이다.</summary>
    internal static string? Seal(HttpContext context, object request, string? position)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(request);
        if (position is null)
        {
            return null;
        }

        ArgumentException.ThrowIfNullOrEmpty(position);
        var envelope = new Envelope(position, Hasher(context).Hash(SigningInput(context, request, position)));
        return Base64Url.EncodeToString(JsonSerializer.SerializeToUtf8Bytes(envelope));
    }

    internal static IResult Invalid() => ErrorHttpResults.FromCode("CURSOR_INVALID", "The cursor is invalid.");

    private static ITokenHasher Hasher(HttpContext context) =>
        context.RequestServices.GetRequiredService<ITokenHasher>();

    private static string SigningInput(HttpContext context, object request, string position)
    {
        var actor = context.RequestServices.GetRequiredService<ICurrentActorAccessor>().CurrentActor;
        var binding = JsonSerializer.SerializeToElement(new
        {
            actor = actor is null
                ? null
                : new
                {
                    actor.ActorType,
                    actor.UserPublicId,
                    actor.SystemRole,
                    actor.McpCredentialPublicId,
                },
            method = context.Request.Method,
            route = (context.GetEndpoint() as RouteEndpoint)?.RoutePattern.RawText,
            routeValues = RouteValues(context),
            request = BoundRequest(request),
            position,
        });
        return SigningPrefix + CanonicalJson.Serialize(binding);
    }

    /// <summary>route 값을 이름 순으로 모으고 UUID는 소문자 하이픈 형식으로 맞춘다.</summary>
    private static SortedDictionary<string, string?> RouteValues(HttpContext context)
    {
        var values = new SortedDictionary<string, string?>(StringComparer.Ordinal);
        foreach (var (key, value) in context.Request.RouteValues)
        {
            var text = Convert.ToString(value, CultureInfo.InvariantCulture);
            values[key] = Guid.TryParse(text, out var publicId) ? publicId.ToString("D") : text;
        }

        return values;
    }

    /// <summary>바인딩된 요청 DTO에서 페이지 이동에 따라 바뀌는 cursor와 limit만 뺀 조회 조건이다.</summary>
    private static Dictionary<string, JsonElement> BoundRequest(object request)
    {
        var element = JsonSerializer.SerializeToElement(request, request.GetType());
        if (element.ValueKind != JsonValueKind.Object)
        {
            throw new ArgumentException("A page request must serialize to a JSON object.", nameof(request));
        }

        return element.EnumerateObject()
            .Where(property => !UnboundRequestProperties.Contains(property.Name, StringComparer.OrdinalIgnoreCase))
            .ToDictionary(property => property.Name, property => property.Value.Clone(), StringComparer.Ordinal);
    }

    private sealed record Envelope(
        [property: JsonPropertyName("p")] string? Position,
        [property: JsonPropertyName("s")] string? Signature);
}
