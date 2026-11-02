using System.Text.Json;
using System.Text.Json.Serialization;

namespace RiftOverlay.Services.Lcu;

/// <summary>One item in a block. The id is a string in the payload, not a number.</summary>
public sealed class ItemSetEntry
{
    public int Count { get; set; } = 1;

    /// <summary>
    /// The item id as text — <c>"1056"</c>, not <c>1056</c>. Confirmed from a live client. Sending
    /// a number here is the sort of mismatch the endpoint accepts quietly and then ignores.
    /// </summary>
    public string Id { get; set; } = string.Empty;
}

public sealed class ItemSetBlock
{
    /// <summary>Free text shown as the block header, e.g. "Starting Items".</summary>
    public string Type { get; set; } = string.Empty;

    public List<ItemSetEntry> Items { get; set; } = [];

    [JsonPropertyName("hideIfSummonerSpell")]
    public string HideIfSummonerSpell { get; set; } = string.Empty;

    [JsonPropertyName("showIfSummonerSpell")]
    public string ShowIfSummonerSpell { get; set; } = string.Empty;
}

public sealed class ItemSet
{
    public string Title { get; set; } = string.Empty;

    public string Type { get; set; } = "custom";

    public string Map { get; set; } = "any";

    public string Mode { get; set; } = "any";

    public string Uid { get; set; } = string.Empty;

    [JsonPropertyName("startedFrom")]
    public string StartedFrom { get; set; } = "blank";

    public int Sortrank { get; set; }

    [JsonPropertyName("associatedChampions")]
    public List<int> AssociatedChampions { get; set; } = [];

    [JsonPropertyName("associatedMaps")]
    public List<int> AssociatedMaps { get; set; } = [];

    [JsonPropertyName("preferredItemSlots")]
    public List<object> PreferredItemSlots { get; set; } = [];

    public List<ItemSetBlock> Blocks { get; set; } = [];
}

/// <summary>
/// The whole collection. <see cref="AccountId"/> and <see cref="Timestamp"/> are carried back
/// unchanged on write — they come from the client and are not ours to invent.
/// </summary>
public sealed class ItemSetCollection
{
    [JsonPropertyName("accountId")]
    public long AccountId { get; set; }

    [JsonPropertyName("itemSets")]
    public List<ItemSet> ItemSets { get; set; } = [];

    [JsonPropertyName("timestamp")]
    public long Timestamp { get; set; }
}

/// <summary>
/// Writes recommended item sets into the League client.
/// <para>
/// <b>This endpoint replaces the user's entire set collection in one request.</b> A live account
/// checked while building this held sixty-one sets, most of them written by another tool — so a
/// naive PUT of just our own set would have silently destroyed all of them, with no undo and no
/// error. Every write here therefore reads the current collection first, edits it, and sends it
/// back whole.
/// </para>
/// <para>
/// Only sets this tool created are removed, identified by the <see cref="ManagedPrefix"/> in their
/// title — the same rule the rune pages follow. A set the user made, or one another tool made, is
/// never touched.
/// </para>
/// </summary>
public sealed class ItemSetService
{
    /// <summary>Titles starting with this belong to us and may be replaced.</summary>
    public const string ManagedPrefix = "RiftOverlay";

    /// <summary>Summoner's Rift.</summary>
    private const int RiftMapId = 11;

    private readonly LcuClient _client;
    private readonly ILog _log;

    public ItemSetService(LcuClient client, ILog? log = null)
    {
        _client = client;
        _log = log ?? NullLog.Instance;
    }

    /// <summary>
    /// Replaces our set for a champion, leaving every other set alone. Returns false when the
    /// client is unreachable or refuses the write; nothing is changed in that case.
    /// </summary>
    public async Task<bool> ApplyAsync(
        int championId, string championName, ItemSet set, CancellationToken token = default)
    {
        var summonerId = await ReadSummonerIdAsync(token).ConfigureAwait(false);

        if (summonerId == 0)
        {
            _log.Warn("summonerId okunamadı; eşya seti yazılmadı.");
            return false;
        }

        var path = $"/lol-item-sets/v1/item-sets/{summonerId}/sets";

        var collection = await _client.GetAsync<ItemSetCollection>(path, token).ConfigureAwait(false);

        if (collection is null)
        {
            _log.Warn("Eşya setleri okunamadı; yazma yapılmadı.");
            return false;
        }

        var before = collection.ItemSets.Count;

        // Read-modify-write: everything not ours survives untouched.
        collection.ItemSets.RemoveAll(s =>
            s.Title.StartsWith(ManagedPrefix, StringComparison.Ordinal) &&
            s.AssociatedChampions.Contains(championId));

        set.Title = $"{ManagedPrefix} · {championName}";
        set.AssociatedChampions = [championId];
        set.AssociatedMaps = [RiftMapId];
        set.Uid = Guid.NewGuid().ToString();

        collection.ItemSets.Add(set);

        var written = await _client.PutAsync(path, collection, token).ConfigureAwait(false);

        _log.Info(written
            ? $"Eşya seti yazıldı: {championName} ({before} setten {collection.ItemSets.Count} sete)"
            : $"Eşya seti yazılamadı: {championName}");

        return written;
    }

    private async Task<long> ReadSummonerIdAsync(CancellationToken token)
    {
        var summoner = await _client
            .GetAsync<JsonElement>("/lol-summoner/v1/current-summoner", token)
            .ConfigureAwait(false);

        return summoner.ValueKind == JsonValueKind.Object &&
               summoner.TryGetProperty("summonerId", out var id) &&
               id.TryGetInt64(out var value)
            ? value
            : 0;
    }

    /// <summary>
    /// Builds a set from a recommendation. Blocks that would be empty are dropped rather than
    /// written as empty headers.
    /// </summary>
    public static ItemSet Build(
        IReadOnlyList<int> starters, IReadOnlyList<int> boots, IReadOnlyList<int> core)
    {
        var set = new ItemSet();

        AddBlock(set, "Başlangıç", starters);
        AddBlock(set, "Çizme", boots);
        AddBlock(set, "Çekirdek", core);

        return set;
    }

    private static void AddBlock(ItemSet set, string title, IReadOnlyList<int> itemIds)
    {
        if (itemIds.Count == 0)
        {
            return;
        }

        // Repeated ids become a count rather than a repeated row — two Health Potions are one
        // entry with count 2, which is how the client's own sets are written.
        var items = itemIds
            .GroupBy(id => id)
            .Select(group => new ItemSetEntry
            {
                Id = group.Key.ToString(),
                Count = group.Count(),
            })
            .ToList();

        set.Blocks.Add(new ItemSetBlock { Type = title, Items = items });
    }
}
