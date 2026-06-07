using System.Text.Json;
using System.Text.Json.Serialization;
using Ez.Handball.Domain;

namespace Ez.Handball.Api.Serialization;

public sealed class TournamentTypeJsonConverter : JsonConverter<TournamentType>
{
    public override TournamentType Read(
        ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var raw = reader.GetString();
        if (TournamentTypes.TryParse(raw, out var type))
            return type;
        throw new JsonException($"Unknown tournament type '{raw}'.");
    }

    public override void Write(
        Utf8JsonWriter writer, TournamentType value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value.ToWireString());
    }
}
