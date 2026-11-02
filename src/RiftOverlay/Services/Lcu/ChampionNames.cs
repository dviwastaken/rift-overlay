namespace RiftOverlay.Services.Lcu;

/// <summary>
/// Champion id to name, read from the client's own asset list.
/// <para>
/// Taking it from the client rather than Data Dragon means it is always the right patch and the
/// right language without a second download to keep in step — the client is already correct by
/// definition.
/// </para>
/// <para>
/// Shared rather than loaded per caller: champion select and player lookup both need it, and two
/// copies would mean two downloads and two chances to drift.
/// </para>
/// </summary>
public sealed class ChampionNames
{
    private const string AssetPath = "/lol-game-data/assets/v1/champion-summary.json";

    private readonly LcuClient? _client;
    private readonly ILog _log;

    private Dictionary<int, string> _names = [];

    public ChampionNames(LcuClient client, ILog? log = null)
    {
        _client = client;
        _log = log ?? NullLog.Instance;
    }

    private ChampionNames(Dictionary<int, string> names)
    {
        _names = names;
        _log = NullLog.Instance;
    }

    /// <summary>
    /// A table that is already known, with no client behind it. Used where the names come from
    /// somewhere else — and by tests, which then need no reflection to reach inside this class.
    /// </summary>
    public static ChampionNames From(IReadOnlyDictionary<int, string> names) =>
        new(names.ToDictionary(pair => pair.Key, pair => pair.Value));

    public bool IsReady => _names.Count > 0;

    /// <summary>Falls back to "#id" so an unknown champion is still identifiable.</summary>
    public string NameOf(int championId) =>
        championId <= 0 ? "—" : _names.TryGetValue(championId, out var name) ? name : $"#{championId}";

    /// <summary>Loads once and stays loaded; repeated calls are free.</summary>
    public async Task EnsureLoadedAsync(CancellationToken token = default)
    {
        if (IsReady || _client is null)
        {
            return;
        }

        var champions = await _client
            .GetAsync<List<ChampionSummary>>(AssetPath, token)
            .ConfigureAwait(false);

        if (champions is null)
        {
            return;
        }

        // Id 0 is the "no champion" placeholder and would collide with the unpicked state.
        _names = champions
            .Where(c => c.Id > 0)
            .ToDictionary(c => c.Id, c => c.Name);

        _log.Info($"Şampiyon listesi alındı: {_names.Count}");
    }
}
