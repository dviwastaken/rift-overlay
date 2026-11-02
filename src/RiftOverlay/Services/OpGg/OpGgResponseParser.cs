using System.IO;
using System.Text;
using System.Text.Json;

namespace RiftOverlay.Services.OpGg;

/// <summary>
/// Turns OP.GG's compact tool output into ordinary JSON so the rest of the code can deserialise it
/// with <see cref="JsonSerializer"/> like every other payload in the project.
/// <para>
/// The server does not return JSON. It returns a schema preamble followed by one constructor-style
/// expression:
/// </para>
/// <code>
/// class Data: summary,core_items,boots
/// class CoreItems: ids,ids_names,play,win,pick_rate
///
/// Data(Summary(61,false),CoreItems([6655],["Luden"],2303,1211,0.12),CoreItems([3020],["Pabuç"],16521,7863,0.47))
/// </code>
/// <para>
/// <b>The trap this parser exists to survive:</b> arguments are positional, and one class name is
/// reused for several different things. In the payload above <c>CoreItems</c> is both the core
/// build and the boots — and elsewhere the starting items and the summoner spells too. Nothing in
/// the value itself distinguishes them; only the <em>parent's</em> declared field order does. So the
/// class declarations are not decoration to be skipped, they are the only key to what each argument
/// means, and reading them is what keeps boots from being stored as a core build.
/// </para>
/// </summary>
public static class OpGgResponseParser
{
    /// <summary>
    /// Case-insensitive to match the rest of the project's readers. This matters more here than
    /// usual: the wire format is entirely snake_case and lower-case, so a case-sensitive read binds
    /// nothing at all and hands back a fully-populated-looking object with every field empty.
    /// </summary>
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    /// <summary>
    /// Parses one compact response into the model. Callers use this rather than
    /// <see cref="ToJson"/> plus their own deserialiser, so the options above cannot be forgotten.
    /// </summary>
    public static ChampionAnalysis Parse(string compact) =>
        JsonSerializer.Deserialize<ChampionAnalysisEnvelope>(ToJson(compact), SerializerOptions)?.Data
        ?? throw new FormatException("OP.GG yanıtı çözümlenemedi.");

    /// <summary>
    /// Converts one compact response to JSON. Throws <see cref="FormatException"/> when the text is
    /// not in this shape at all, so a changed protocol surfaces instead of yielding empty objects.
    /// </summary>
    public static string ToJson(string compact)
    {
        if (string.IsNullOrWhiteSpace(compact))
        {
            throw new FormatException("OP.GG yanıtı boş.");
        }

        var classes = ReadClasses(compact, out var valueStart);

        if (valueStart < 0)
        {
            throw new FormatException("OP.GG yanıtında değer ifadesi bulunamadı.");
        }

        var buffer = new MemoryStream();

        using (var writer = new Utf8JsonWriter(buffer))
        {
            var reader = new Cursor(compact, valueStart);
            WriteValue(ref reader, classes, writer);
            writer.Flush();
        }

        return Encoding.UTF8.GetString(buffer.ToArray());
    }

    /// <summary>
    /// Collects the <c>class Name: a,b,c</c> preamble and reports where the value expression starts.
    /// </summary>
    private static Dictionary<string, string[]> ReadClasses(string text, out int valueStart)
    {
        var classes = new Dictionary<string, string[]>(StringComparer.Ordinal);
        valueStart = -1;

        var position = 0;

        while (position < text.Length)
        {
            var lineEnd = text.IndexOf('\n', position);
            var end = lineEnd < 0 ? text.Length : lineEnd;
            var line = text[position..end].Trim();

            if (line.Length > 0)
            {
                if (line.StartsWith("class ", StringComparison.Ordinal))
                {
                    var colon = line.IndexOf(':');

                    if (colon > 0)
                    {
                        var name = line["class ".Length..colon].Trim();
                        var fields = line[(colon + 1)..]
                            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

                        classes[name] = fields;
                    }
                }
                else
                {
                    // First non-class, non-blank line begins the value.
                    valueStart = position + (text[position..end].Length - text[position..end].TrimStart().Length);
                    return classes;
                }
            }

            if (lineEnd < 0)
            {
                break;
            }

            position = lineEnd + 1;
        }

        return classes;
    }

    private static void WriteValue(ref Cursor cursor, Dictionary<string, string[]> classes, Utf8JsonWriter writer)
    {
        cursor.SkipWhitespace();

        if (cursor.AtEnd)
        {
            throw new FormatException("OP.GG yanıtı beklenmedik şekilde bitti.");
        }

        switch (cursor.Current)
        {
            case '[':
                cursor.Advance();
                writer.WriteStartArray();

                while (true)
                {
                    cursor.SkipWhitespace();

                    if (cursor.AtEnd)
                    {
                        throw new FormatException("Kapanmayan dizi.");
                    }

                    if (cursor.Current == ']')
                    {
                        cursor.Advance();
                        break;
                    }

                    if (cursor.Current == ',')
                    {
                        cursor.Advance();
                        continue;
                    }

                    WriteValue(ref cursor, classes, writer);
                }

                writer.WriteEndArray();
                return;

            case '"':
                writer.WriteStringValue(cursor.ReadString());
                return;
        }

        if (char.IsLetter(cursor.Current) || cursor.Current == '_')
        {
            var name = cursor.ReadIdentifier();
            cursor.SkipWhitespace();

            if (!cursor.AtEnd && cursor.Current == '(')
            {
                cursor.Advance();
                WriteObject(ref cursor, classes, writer, name);
                return;
            }

            WriteLiteral(writer, name);
            return;
        }

        writer.WriteRawValue(cursor.ReadNumber());
    }

    private static void WriteObject(
        ref Cursor cursor, Dictionary<string, string[]> classes, Utf8JsonWriter writer, string className)
    {
        // An undeclared class is not fatal: the arguments still parse, they just get positional
        // names. Losing one object's field names beats failing the whole response.
        var fields = classes.TryGetValue(className, out var declared) ? declared : [];

        writer.WriteStartObject();

        var index = 0;

        while (true)
        {
            cursor.SkipWhitespace();

            if (cursor.AtEnd)
            {
                throw new FormatException($"Kapanmayan nesne: {className}");
            }

            if (cursor.Current == ')')
            {
                cursor.Advance();
                break;
            }

            if (cursor.Current == ',')
            {
                cursor.Advance();
                continue;
            }

            writer.WritePropertyName(index < fields.Length ? fields[index] : $"field{index}");
            WriteValue(ref cursor, classes, writer);
            index++;
        }

        writer.WriteEndObject();
    }

    private static void WriteLiteral(Utf8JsonWriter writer, string token)
    {
        switch (token)
        {
            case "true" or "True":
                writer.WriteBooleanValue(true);
                return;

            case "false" or "False":
                writer.WriteBooleanValue(false);
                return;

            case "null" or "None":
                writer.WriteNullValue();
                return;

            default:
                // A bare word that is not a known literal is still text, not a parse failure.
                writer.WriteStringValue(token);
                return;
        }
    }

    private ref struct Cursor(string text, int position)
    {
        private readonly string _text = text;
        private int _position = position;

        public readonly bool AtEnd => _position >= _text.Length;

        public readonly char Current => _text[_position];

        public void Advance() => _position++;

        public void SkipWhitespace()
        {
            while (_position < _text.Length && char.IsWhiteSpace(_text[_position]))
            {
                _position++;
            }
        }

        public string ReadIdentifier()
        {
            var start = _position;

            while (_position < _text.Length && (char.IsLetterOrDigit(_text[_position]) || _text[_position] == '_'))
            {
                _position++;
            }

            return _text[start.._position];
        }

        /// <summary>
        /// Reads a quoted string. Champion and item names arrive localised, so this has to carry
        /// apostrophes and non-ASCII through untouched — "Luden'in Feryadı" is one string, not a
        /// quote that ends early.
        /// </summary>
        public string ReadString()
        {
            _position++;

            var builder = new StringBuilder();

            while (_position < _text.Length && _text[_position] != '"')
            {
                if (_text[_position] == '\\' && _position + 1 < _text.Length)
                {
                    _position++;

                    builder.Append(_text[_position] switch
                    {
                        'n' => '\n',
                        't' => '\t',
                        'r' => '\r',
                        var other => other,
                    });
                }
                else
                {
                    builder.Append(_text[_position]);
                }

                _position++;
            }

            if (_position >= _text.Length)
            {
                throw new FormatException("Kapanmayan metin değeri.");
            }

            _position++;

            return builder.ToString();
        }

        public string ReadNumber()
        {
            var start = _position;

            while (_position < _text.Length && (char.IsDigit(_text[_position]) ||
                   _text[_position] is '-' or '+' or '.' or 'e' or 'E'))
            {
                _position++;
            }

            var token = _text[start.._position];

            if (token.Length == 0)
            {
                throw new FormatException($"Sayı beklenirken '{_text[_position]}' bulundu.");
            }

            return token;
        }
    }
}
