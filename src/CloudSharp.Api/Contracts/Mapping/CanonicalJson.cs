using System.Buffers;
using System.Text;
using System.Text.Json;

namespace CloudSharp.Api.Contracts.Mapping;

/// <summary>
/// 객체 속성을 이름 순으로 정렬한 JSON을 만든다. 같은 입력이면 속성 순서와 관계없이 같은 문자열이 되므로
/// 요청 지문과 서명 입력에 사용한다.
/// </summary>
internal static class CanonicalJson
{
    internal static string Serialize(JsonElement element)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            Write(writer, element);
        }

        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }

    internal static void Write(Utf8JsonWriter writer, JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();
                foreach (var property in element.EnumerateObject().OrderBy(static property => property.Name, StringComparer.Ordinal))
                {
                    writer.WritePropertyName(property.Name);
                    Write(writer, property.Value);
                }
                writer.WriteEndObject();
                break;
            case JsonValueKind.Array:
                writer.WriteStartArray();
                foreach (var item in element.EnumerateArray())
                {
                    Write(writer, item);
                }
                writer.WriteEndArray();
                break;
            default:
                element.WriteTo(writer);
                break;
        }
    }
}
