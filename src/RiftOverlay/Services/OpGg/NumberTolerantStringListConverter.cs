using System.Text.Json;
using System.Text.Json.Serialization;

namespace RiftOverlay.Services.OpGg;

/// <summary>
/// Reads a list of names that is not always a list of names.
/// <para>
/// OP.GG sends item, boot and starting-item rows with localised strings in <c>ids_names</c> — but
/// the summoner-spell row, which uses the identical wire shape, repeats the numeric ids there
/// instead: <c>CoreItems([4,12],[4,12],…)</c>. A plain <c>List&lt;string&gt;</c> throws on that one
/// row and takes the entire response down with it, so the whole champion looks like it has no
/// build at all because of two numbers in one field.
/// </para>
/// <para>
/// Numbers are kept as their text rather than dropped: the caller can still resolve id 4 to Flash
/// through the catalogue, and silently discarding them would hide that the row arrived unnamed.
/// </para>
/// </summary>
public sealed class NumberTolerantStringListConverter : JsonConverter<List<string>>
{
    public override List<string> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var values = new List<string>();

        if (reader.TokenType == JsonTokenType.Null)
        {
            return values;
        }

        if (reader.TokenType != JsonTokenType.StartArray)
        {
            throw new JsonException("İsim listesi dizi olarak bekleniyordu.");
        }

        while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
        {
            values.Add(reader.TokenType switch
            {
                JsonTokenType.String => reader.GetString() ?? string.Empty,
                JsonTokenType.Number => reader.GetDouble().ToString(System.Globalization.CultureInfo.InvariantCulture),
                JsonTokenType.Null => string.Empty,
                _ => throw new JsonException($"İsim listesinde beklenmedik değer: {reader.TokenType}"),
            });
        }

        return values;
    }

    public override void Write(Utf8JsonWriter writer, List<string> value, JsonSerializerOptions options)
    {
        writer.WriteStartArray();

        foreach (var item in value)
        {
            writer.WriteStringValue(item);
        }

        writer.WriteEndArray();
    }
}
