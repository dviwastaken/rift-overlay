using System.IO;
using System.Text.Json;
using System.Net.Http;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace RiftOverlay.Services;

/// <summary>
/// Fetches item artwork from Data Dragon and keeps it, first on disk and then in memory.
/// <para>
/// Icons are per-patch, so the cache is keyed by the catalogue's version — asking for last patch's
/// artwork with this patch's item ids is how an overlay ends up showing the wrong picture next to
/// the right name.
/// </para>
/// <para>
/// Every failure path returns null rather than throwing. A missing icon must cost the overlay an
/// image, never a frame: the panels are readable without artwork, and an exception on the render
/// path during a teamfight is far worse than a blank square.
/// </para>
/// </summary>
public sealed class IconCache : IDisposable
{
    /// <summary>
    /// Icons are drawn at roughly 20 pixels, so they are decoded small. Full-size decoding would
    /// hold a 64×64 bitmap per item for no visible gain across a whole match's worth of items.
    /// </summary>
    private const int DecodeWidth = 48;

    private readonly string _directory;
    private readonly ILog _log;
    private readonly HttpClient _http;
    private readonly Dictionary<int, ImageSource?> _memory = [];
    private readonly Dictionary<int, ImageSource?> _champions = [];

    /// <summary>
    /// Fetches that have not settled yet, so the two ways of asking share one request rather than
    /// racing each other for the same picture.
    /// </summary>
    private readonly Dictionary<int, Task<ImageSource?>> _pending = [];
    private readonly SemaphoreSlim _gate = new(1, 1);

    private readonly SemaphoreSlim _keyGate = new(1, 1);

    /// <summary>Numeric champion id to the name Data Dragon files its artwork under.</summary>
    private Dictionary<int, string>? _championKeys;

    private string _version = string.Empty;

    public IconCache(string? cacheDirectory = null, ILog? log = null, HttpMessageHandler? handler = null)
    {
        _log = log ?? NullLog.Instance;

        _directory = cacheDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "RiftOverlay",
            "ddragon",
            "icons");

        _http = handler is null ? new HttpClient() : new HttpClient(handler);
        _http.Timeout = TimeSpan.FromSeconds(10);
    }

    /// <summary>
    /// Points the cache at a patch. Changing it clears what is held in memory, because the same id
    /// can mean a different item after a patch.
    /// </summary>
    public void UseVersion(string version)
    {
        if (string.Equals(_version, version, StringComparison.Ordinal))
        {
            return;
        }

        _version = version;
        _memory.Clear();
        _champions.Clear();
        _pending.Clear();
        _championKeys = null;
    }

    public bool IsReady => _version.Length > 0;

    /// <summary>
    /// Returns a frozen, thread-safe image, or null when there is none to be had. Results are
    /// remembered either way — including the misses, so a deleted item id is not re-requested on
    /// every poll for the rest of the match.
    /// </summary>
    public async Task<ImageSource?> GetItemIconAsync(int itemId, CancellationToken token = default)
    {
        if (itemId <= 0 || !IsReady)
        {
            return null;
        }

        if (_memory.TryGetValue(itemId, out var cached))
        {
            return cached;
        }

        await _gate.WaitAsync(token).ConfigureAwait(false);

        try
        {
            if (_memory.TryGetValue(itemId, out cached))
            {
                return cached;
            }

            var image = await LoadAsync(itemId, token).ConfigureAwait(false);
            _memory[itemId] = image;

            return image;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            _log.Warn($"İkon alınamadı ({itemId}): {exception.Message}");
            _memory[itemId] = null;

            return null;
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<ImageSource?> LoadAsync(int itemId, CancellationToken token)
    {
        var folder = Path.Combine(_directory, _version);
        var file = Path.Combine(folder, $"{itemId}.png");

        if (File.Exists(file))
        {
            return Decode(await File.ReadAllBytesAsync(file, token).ConfigureAwait(false));
        }

        var url = $"https://ddragon.leagueoflegends.com/cdn/{_version}/img/item/{itemId}.png";

        using var response = await _http.GetAsync(url, token).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        var bytes = await response.Content.ReadAsByteArrayAsync(token).ConfigureAwait(false);

        Directory.CreateDirectory(folder);
        await File.WriteAllBytesAsync(file, bytes, token).ConfigureAwait(false);

        return Decode(bytes);
    }

    /// <summary>
    /// Decodes and freezes so the result can be handed to the UI thread from wherever the download
    /// finished. An unfrozen BitmapImage belongs to the thread that made it and throws when bound
    /// from another one.
    /// </summary>
    private static ImageSource? Decode(byte[] bytes)
    {
        if (bytes.Length == 0)
        {
            return null;
        }

        using var stream = new MemoryStream(bytes);

        var image = new BitmapImage();

        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.DecodePixelWidth = DecodeWidth;
        image.StreamSource = stream;
        image.EndInit();
        image.Freeze();

        return image;
    }

    /// <summary>
    /// Hands back a champion portrait only if the answer is already known, and starts fetching it
    /// otherwise.
    /// <para>
    /// Synchronous on purpose. Champion select is polled once a second and its rows are compared by
    /// value to avoid rebuilding the list on every tick; an icon that arrived through a mutable
    /// property would either break that comparison or never be noticed by it. Returning what is in
    /// hand means the row simply changes on the next poll once the artwork lands, and the list
    /// rebuilds exactly once.
    /// </para>
    /// </summary>
    /// <returns>True when a decision is cached — including a cached failure, so a champion whose
    /// artwork cannot be had is not requested again every second for the rest of the draft.</returns>
    public bool TryGetChampionIcon(int championId, out ImageSource? icon)
    {
        icon = null;

        if (championId <= 0 || !IsReady)
        {
            return true;
        }

        lock (_champions)
        {
            if (_champions.TryGetValue(championId, out icon))
            {
                return true;
            }
        }

        _ = Start(championId);

        return false;
    }

    /// <summary>
    /// Waits for a portrait instead of glancing at the cache.
    /// <para>
    /// The player pane needs this and champion select does not: a draft is polled once a second and
    /// picks the artwork up on the next tick, while a lookup builds its rows once and never again.
    /// Without a way to wait, the first search of a session showed a blank square beside every
    /// match and only a second search filled them in.
    /// </para>
    /// </summary>
    public Task<ImageSource?> GetChampionIconAsync(int championId)
    {
        if (championId <= 0 || !IsReady)
        {
            return Task.FromResult<ImageSource?>(null);
        }

        lock (_champions)
        {
            if (_champions.TryGetValue(championId, out var cached))
            {
                return Task.FromResult(cached);
            }
        }

        return Start(championId);
    }

    /// <summary>
    /// Hands back the fetch already running for this champion, or starts the only one.
    /// <para>
    /// The slot is claimed before any work begins, and that ordering is the whole point. Starting
    /// the fetch first and recording it afterwards looks equivalent and is not: a request that
    /// fails synchronously — an offline machine throws before it ever awaits — runs its own cleanup
    /// while the slot is still empty, and the finished, failed task is then written into the slot
    /// it was supposed to have vacated. Every later ask is handed that corpse and the portrait
    /// never loads again, however many times the network comes back.
    /// </para>
    /// </summary>
    private Task<ImageSource?> Start(int championId)
    {
        TaskCompletionSource<ImageSource?> completion;

        lock (_champions)
        {
            if (_champions.TryGetValue(championId, out var cached))
            {
                return Task.FromResult(cached);
            }

            if (_pending.TryGetValue(championId, out var running))
            {
                return running;
            }

            completion = new TaskCompletionSource<ImageSource?>(
                TaskCreationOptions.RunContinuationsAsynchronously);

            _pending[championId] = completion.Task;
        }

        _ = CompleteAsync(championId, completion);

        return completion.Task;
    }

    private async Task CompleteAsync(int championId, TaskCompletionSource<ImageSource?> completion)
    {
        // The fetch swallows its own failures, so this never faults the task: a caller waiting on a
        // portrait gets null, not an exception it would have to guard.
        completion.SetResult(await FetchChampionAsync(championId).ConfigureAwait(false));
    }

    private async Task<ImageSource?> FetchChampionAsync(int championId)
    {
        ImageSource? image = null;

        // A settled answer is remembered; a failed attempt is not.
        //
        // Caching the failure was the first version and it was wrong in a way that only showed up
        // on a cold cache: several portraits are asked for at once, they all raced to write the
        // same key table, the losers threw on the file, and every one of them recorded a permanent
        // "this champion has no artwork" for the rest of the session. The draft rendered blank
        // squares and a retry would have fixed it. Now a thrown attempt simply leaves the slot
        // empty, and the next poll a second later tries again.
        var settled = true;

        try
        {
            image = await LoadChampionAsync(championId).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            // A missing portrait costs a picture, never a frame — and never the process, which is
            // what an unobserved exception on a fire-and-forget task would cost.
            _log.Warn($"Şampiyon ikonu alınamadı ({championId}): {exception.Message}");
            settled = false;
        }
        finally
        {
            lock (_champions)
            {
                if (settled)
                {
                    _champions[championId] = image;
                }

                _pending.Remove(championId);
            }
        }

        return image;
    }

    private async Task<ImageSource?> LoadChampionAsync(int championId)
    {
        var folder = Path.Combine(_directory, _version);
        var file = Path.Combine(folder, $"champ-{championId}.png");

        if (File.Exists(file))
        {
            return Decode(await File.ReadAllBytesAsync(file).ConfigureAwait(false));
        }

        var keys = await EnsureChampionKeysAsync().ConfigureAwait(false);

        if (keys is null || !keys.TryGetValue(championId, out var key))
        {
            return null;
        }

        var url = $"https://ddragon.leagueoflegends.com/cdn/{_version}/img/champion/{key}.png";

        using var response = await _http.GetAsync(url).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        var bytes = await response.Content.ReadAsByteArrayAsync().ConfigureAwait(false);

        Directory.CreateDirectory(folder);
        await File.WriteAllBytesAsync(file, bytes).ConfigureAwait(false);

        return Decode(bytes);
    }

    /// <summary>
    /// Loads the numeric id to artwork-name table, from disk if it is there and from Data Dragon
    /// otherwise.
    /// <para>
    /// The table is not optional and cannot be guessed from a champion's displayed name: Wukong's
    /// artwork is filed as MonkeyKing, Nunu ve Willump as Nunu, Dr. Mundo as DrMundo, Kai'Sa as
    /// Kaisa. Stripping punctuation from the name would silently lose those six and show a blank
    /// square for them alone.
    /// </para>
    /// </summary>
    private async Task<Dictionary<int, string>?> EnsureChampionKeysAsync()
    {
        if (_championKeys is not null)
        {
            return _championKeys;
        }

        // One loader at a time. Ten portraits are asked for at the same moment in a draft, and
        // without this they all fetch and all write.
        await _keyGate.WaitAsync().ConfigureAwait(false);

        try
        {
            if (_championKeys is not null)
            {
                return _championKeys;
            }

            return await LoadChampionKeysAsync().ConfigureAwait(false);
        }
        finally
        {
            _keyGate.Release();
        }
    }

    private async Task<Dictionary<int, string>?> LoadChampionKeysAsync()
    {
        var folder = Path.Combine(_directory, _version);
        var file = Path.Combine(folder, "champions.json");

        // Failures are deliberately not caught here.
        //
        // The first version swallowed them and returned "no table", which the caller could not tell
        // apart from "the table loaded and this champion is not in it" — so an offline moment was
        // recorded as a permanent verdict on the champion. Letting it throw keeps the two apart:
        // an unreachable table is retried, a champion genuinely absent from a table that did load
        // is not.
        var json = File.Exists(file)
            ? await File.ReadAllTextAsync(file).ConfigureAwait(false)
            : await FetchChampionKeysAsync(folder, file).ConfigureAwait(false);

        if (json is null)
        {
            return null;
        }

        using var document = JsonDocument.Parse(json);

        var map = new Dictionary<int, string>();

        foreach (var entry in document.RootElement.GetProperty("data").EnumerateObject())
        {
            // "key" is the numeric id as a string; "id" is the artwork name.
            if (int.TryParse(entry.Value.GetProperty("key").GetString(), out var id))
            {
                map[id] = entry.Value.GetProperty("id").GetString() ?? entry.Name;
            }
        }

        return _championKeys = map;
    }

    private async Task<string?> FetchChampionKeysAsync(string folder, string file)
    {
        var url = $"https://ddragon.leagueoflegends.com/cdn/{_version}/data/en_US/champion.json";

        using var response = await _http.GetAsync(url).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

        // Stored beside the artwork it explains, so the patch pruner removes both together.
        // Written through a temporary name: a reader that arrives mid-write must not find half a
        // file and cache a table with half the champions in it.
        Directory.CreateDirectory(folder);

        var staging = Path.Combine(folder, $"champions.{Guid.NewGuid():N}.tmp");

        await File.WriteAllTextAsync(staging, json).ConfigureAwait(false);
        File.Move(staging, file, overwrite: true);

        return json;
    }

    public void Dispose()
    {
        _http.Dispose();
        _gate.Dispose();
        _keyGate.Dispose();
    }
}
