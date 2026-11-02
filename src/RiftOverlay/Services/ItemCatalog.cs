using System.IO;
using System.Net.Http;
using System.Text.Json;

namespace RiftOverlay.Services;

public sealed record ItemInfo(int Id, string Name, int TotalGold, IReadOnlyList<int> From, bool IsBuildTarget);

public interface IItemCatalog
{
    bool IsReady { get; }

    bool TryGet(int id, out ItemInfo item);

    IReadOnlyList<ItemInfo> BuildTargets { get; }

    /// <summary>
    /// The patch the loaded catalogue belongs to, e.g. "16.17.1"; empty until something is loaded.
    /// Icon URLs are versioned, so anything fetching artwork has to use the same patch the item
    /// list came from — otherwise a cached offline start would ask the CDN for icons from a patch
    /// whose item ids no longer match.
    /// </summary>
    string Version { get; }
}

/// <summary>
/// Item costs and recipes from Riot's Data Dragon CDN — the Live Client API only prices items a
/// player already owns, so anything about what an item *will* cost has to come from here.
/// Free, keyless and versioned; downloaded once per patch and cached on disk so a later offline
/// start still works.
/// </summary>
public sealed class DataDragonItemCatalog : IItemCatalog
{
    private const string VersionsUrl = "https://ddragon.leagueoflegends.com/api/versions.json";

    /// <summary>Summoner's Rift. Items flagged for other maps would pollute the candidate list.</summary>
    private const string RiftMapId = "11";

    /// <summary>Used when the requested locale cannot be fetched — always present on the CDN.</summary>
    private const string FallbackLocale = "en_US";

    private readonly string _cacheDirectory;
    private readonly string _locale;
    private readonly ILog _log;

    private Dictionary<int, ItemInfo> _items = [];
    private List<ItemInfo> _buildTargets = [];
    private string _version = string.Empty;

    public DataDragonItemCatalog(string? cacheDirectory = null, string locale = FallbackLocale, ILog? log = null)
    {
        _log = log ?? NullLog.Instance;

        _cacheDirectory = cacheDirectory ?? DefaultCacheDirectory;

        _locale = locale;
    }

    /// <summary>Where the item list, spell list and icons are cached, shared with the pruner.</summary>
    public static string DefaultCacheDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "RiftOverlay",
        "ddragon");

    public bool IsReady => _items.Count > 0;

    public string Version => _version;

    public IReadOnlyList<ItemInfo> BuildTargets => _buildTargets;

    public bool TryGet(int id, out ItemInfo item) => _items.TryGetValue(id, out item!);

    /// <summary>
    /// Fetches the current patch's item list, falling back to the newest cached copy when the CDN
    /// is unreachable. Never throws: without a catalogue the build panel simply stays hidden.
    /// </summary>
    public async Task LoadAsync(CancellationToken token = default)
    {
        foreach (var locale in Locales())
        {
            var json = await TryDownloadAsync(locale, token).ConfigureAwait(false) ?? TryReadCache(locale);

            if (json is null)
            {
                continue;
            }

            try
            {
                Parse(json);
            }
            catch (Exception exception)
            {
                // A truncated cache file or a reshaped payload leaves the feature off rather than
                // taking the overlay down.
                _log.Error($"Item kataloğu ayrıştırılamadı ({locale})", exception);
                continue;
            }

            if (IsReady)
            {
                _log.Info($"Item kataloğu hazır: {_items.Count} item, {_buildTargets.Count} build hedefi ({locale})");
                return;
            }

            _log.Warn($"Item kataloğu boş çıktı ({locale})");
        }

        _log.Warn("Item kataloğu hiçbir dilde yüklenemedi — hedef eşya paneli kapalı kalacak.");
    }

    /// <summary>
    /// The requested locale first, then English. Item names are matched by id, so a fallback only
    /// costs the reader a language — never a wrong price.
    /// </summary>
    private IEnumerable<string> Locales()
    {
        yield return _locale;

        if (!string.Equals(_locale, FallbackLocale, StringComparison.OrdinalIgnoreCase))
        {
            yield return FallbackLocale;
        }
    }

    private async Task<string?> TryDownloadAsync(string locale, CancellationToken token)
    {
        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };

            var versionsJson = await client.GetStringAsync(VersionsUrl, token).ConfigureAwait(false);
            using var versions = JsonDocument.Parse(versionsJson);

            var version = versions.RootElement[0].GetString();
            if (string.IsNullOrWhiteSpace(version))
            {
                return null;
            }

            var cached = Path.Combine(_cacheDirectory, $"items-{version}-{locale}.json");
            if (File.Exists(cached))
            {
                return await File.ReadAllTextAsync(cached, token).ConfigureAwait(false);
            }

            var url = $"https://ddragon.leagueoflegends.com/cdn/{version}/data/{locale}/item.json";
            var json = await client.GetStringAsync(url, token).ConfigureAwait(false);

            Directory.CreateDirectory(_cacheDirectory);
            await File.WriteAllTextAsync(cached, json, token).ConfigureAwait(false);

            return json;
        }
        catch (Exception exception)
        {
            _log.Warn($"Data Dragon indirilemedi ({locale}): {exception.Message}");
            return null;
        }
    }

    private string? TryReadCache(string locale)
    {
        try
        {
            if (!Directory.Exists(_cacheDirectory))
            {
                return null;
            }

            var newest = new DirectoryInfo(_cacheDirectory)
                .GetFiles($"items-*-{locale}.json")
                .OrderByDescending(f => f.LastWriteTimeUtc)
                .FirstOrDefault();

            if (newest is null)
            {
                return null;
            }

            _log.Info($"Önbellekteki katalog kullanılıyor: {newest.Name}");
            return File.ReadAllText(newest.FullName);
        }
        catch (Exception exception)
        {
            _log.Error($"Katalog önbelleği okunamadı ({locale})", exception);
            return null;
        }
    }

    public void Parse(string json)
    {
        using var document = JsonDocument.Parse(json);

        // Read from the payload rather than remembered from the download: this is the only way the
        // version is still right when the catalogue came off the disk cache with no network at all.
        _version = document.RootElement.TryGetProperty("version", out var version)
            ? version.GetString() ?? string.Empty
            : string.Empty;

        var items = new Dictionary<int, ItemInfo>();

        foreach (var entry in document.RootElement.GetProperty("data").EnumerateObject())
        {
            if (!int.TryParse(entry.Name, out var id))
            {
                continue;
            }

            var value = entry.Value;

            if (!IsOnRift(value) || !IsPurchasable(value))
            {
                continue;
            }

            var total = value.TryGetProperty("gold", out var gold) && gold.TryGetProperty("total", out var t)
                ? t.GetInt32()
                : 0;

            var from = ReadIds(value, "from");
            var into = ReadIds(value, "into");

            // A finished item is one nothing else builds out of, that itself has a recipe —
            // which excludes both raw components and consumables in one test.
            var isTarget = into.Count == 0 && from.Count > 0 && total > 0;

            items[id] = new ItemInfo(
                id,
                value.TryGetProperty("name", out var name) ? name.GetString() ?? $"#{id}" : $"#{id}",
                total,
                from,
                isTarget);
        }

        _items = items;
        _buildTargets = [.. items.Values.Where(i => i.IsBuildTarget).OrderBy(i => i.Name)];
    }

    private static bool IsOnRift(JsonElement item) =>
        !item.TryGetProperty("maps", out var maps) ||
        !maps.TryGetProperty(RiftMapId, out var rift) ||
        rift.GetBoolean();

    private static bool IsPurchasable(JsonElement item) =>
        !item.TryGetProperty("gold", out var gold) ||
        !gold.TryGetProperty("purchasable", out var purchasable) ||
        purchasable.GetBoolean();

    private static List<int> ReadIds(JsonElement item, string property)
    {
        if (!item.TryGetProperty(property, out var array) || array.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var ids = new List<int>();

        foreach (var element in array.EnumerateArray())
        {
            if (int.TryParse(element.GetString(), out var id))
            {
                ids.Add(id);
            }
        }

        return ids;
    }
}
