using System.Text.Json;
using System.Text.Json.Serialization;
using CloudSharp.Api.Contracts.Mapping;

namespace CloudSharp.Api.Endpoints.Memberships.Requests;

public sealed record ReplaceSpaceMemberRolesRequest(
    [property: JsonConverter(typeof(RolePublicIdsJsonConverter))] Guid[]? RolePublicIds = null);

internal sealed class RolePublicIdsJsonConverter : JsonConverter<Guid[]>
{
    public RolePublicIdsJsonConverter() { }

    public override Guid[]? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        // Return null for invalid field values so the endpoint can use the Role-specific error detail.
        using var document = JsonDocument.ParseValue(ref reader);
        var roles = document.RootElement;
        if (roles.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        var ids = new Guid[roles.GetArrayLength()];
        var index = 0;
        foreach (var role in roles.EnumerateArray())
        {
            if (role.ValueKind != JsonValueKind.String || !PublicIdParser.TryParse(role.GetString(), out ids[index]))
            {
                return null;
            }

            index++;
        }

        return ids;
    }

    public override void Write(Utf8JsonWriter writer, Guid[] value, JsonSerializerOptions options) =>
        JsonSerializer.Serialize(writer, value, options);
}
