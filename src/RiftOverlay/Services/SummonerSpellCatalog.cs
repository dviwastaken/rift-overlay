using System.IO;
using System.Net.Http;
using System.Text.Json;

namespace RiftOverlay.Services;

/// <summary>
/// Maps summoner-spell ids to their localised names.
/// <para>
/// This exists for one reason: OP.GG returns spells as bare numbers where every other row carries
/// names — a build otherwise reads "4 → 12" next to "Luden'in Feryadı → Gölgealev". The ids are
/// stable, so one small lookup fixes it.
/// </para>
/// <para>
/// Note the shape of Riot's file: it is keyed by the string id (<c>SummonerFlash</c>) while the
/// number everything else uses lives in a <c>key</c> field <em>inside</em> each entry. Indexing by
/// the outer key would build a lookup that never matches anything.
/// </para>
/// </summary>
public sealed class SummonerSpellCatalog
{
    private const string FallbackLocale = "en_US";

    private readonly string _cacheDirectory;
    private readonly string _locale;
    private readonly ILog _log;

    private Dictionary<int, string> _names = [];

    public SummonerSpellCatalog(string? cacheDirectory = null, string locale = FallbackLocale, ILog? log = null)
    {
        _log = log ?? NullLog.Instance;
        _locale = locale;

        _cacheDirectory = cacheDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "RiftOverlay",
            "ddragon");
    }

    public bool IsReady => _names.Count > 0;

    /// <summary>Falls back to the id as text, so an unknown spell still shows something stable.</summary>
    public string NameOf(int key) =>
        _names.TryGetValue(key, out var name) ? name : key.ToString();

    /// <summary>Never throws: without this catalogue spells simply read as numbers.</summary>
    public async Task LoadAsync(string version, CancellationToken token = default)
    {
        if (string.IsNullOrWhiteSpace(version))
        {
            return;
        }

        foreach (var locale in Locales())
        {
            var json = await TryReadAsync(version, locale, token).ConfigureAwait(false);

            if (json is null)
            {
                continue;
            }

            try
            {
                Parse(json);

                if (IsReady)
                {
                    _log.Info($"Sihirdar büyüsü kataloğu hazır: {_names.Count} büyü ({locale})");
                    return;
                }
            }
            catch (Exception exception)
            {
                _log.Warn($"Sihirdar büyüsü kataloğu okunamadı ({locale}): {exception.Message}");
            }
        }
    }

    public void Parse(string json)
    {
        using var document = JsonDocument.Parse(json);

        var names = new Dictionary<int, string>();

        foreach (var entry in document.RootElement.GetProperty("data").EnumerateObject())
        {
            // The numeric id is inside the entry, not the property name.
            if (entry.Value.TryGetProperty("key", out var key) &&
                int.TryParse(key.GetString(), out var id) &&
                entry.Value.TryGetProperty("name", out var name))
            {
                names[id] = name.GetString() ?? string.Empty;
            }
        }

        _names = names;
    }

    private IEnumerable<string> Locales()
    {
        yield return _locale;

        if (!string.Equals(_locale, FallbackLocale, StringComparison.OrdinalIgnoreCase))
        {
            yield return FallbackLocale;
        }
    }

    private async Task<string?> TryReadAsync(string version, string locale, CancellationToken token)
    {
        var cached = Path.Combine(_cacheDirectory, $"spells-{version}-{locale}.json");

        try
        {
            if (File.Exists(cached))
            {
                return await File.ReadAllTextAsync(cached, token).ConfigureAwait(false);
            }

            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };

            var url = $"https://ddragon.leagueoflegends.com/cdn/{version}/data/{locale}/summoner.json";
            var json = await client.GetStringAsync(url, token).ConfigureAwait(false);

            Directory.CreateDirectory(_cacheDirectory);
            await File.WriteAllTextAsync(cached, json, token).ConfigureAwait(false);

            return json;
        }
        catch (Exception exception)
        {
            _log.Warn($"Sihirdar büyüsü listesi indirilemedi ({locale}): {exception.Message}");
            return null;
        }
    }
}
