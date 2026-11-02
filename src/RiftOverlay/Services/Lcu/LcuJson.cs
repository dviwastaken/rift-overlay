using System.Text.Json;
using System.Text.Json.Serialization;

namespace RiftOverlay.Services.Lcu;

/// <summary>
/// The one serializer used for every LCU request and response, in both directions.
/// <para>
/// It is shared rather than duplicated because of a bug that cost real data. Reading used
/// case-insensitive options while writing used <c>JsonSerializer.Serialize(body)</c> with no
/// options at all — so bodies went out with .NET's PascalCase property names. The client accepts
/// such a body with 200 and silently keeps only the fields it recognises. Writing an item set that
/// way produced a set with no title and no blocks, and because that endpoint replaces the whole
/// collection, it stripped the title and blocks off a set the user already had.
/// </para>
/// <para>
/// The verification missed it for the same reason: the probe checking the round-trip built its own
/// options with a camelCase policy, so it validated a serializer the write path never used. Any
/// check of what goes over this wire has to go through <em>this</em> instance, or it is checking
/// something else.
/// </para>
/// </summary>
public static class LcuJson
{
    public static JsonSerializerOptions Options { get; } = new()
    {
        // Reading: the client answers in camelCase and our properties are PascalCase.
        PropertyNameCaseInsensitive = true,

        // Writing: without this, property names go out as written in C# and the client drops them.
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,

        NumberHandling = JsonNumberHandling.AllowReadingFromString,
    };
}
