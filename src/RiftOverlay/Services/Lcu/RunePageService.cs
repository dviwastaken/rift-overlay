using System.Text.Json.Serialization;

namespace RiftOverlay.Services.Lcu;

/// <summary>Shape confirmed against a live client's /lol-perks/v1/pages response.</summary>
public sealed class RunePage
{
    public long Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public int PrimaryStyleId { get; set; }

    public int SubStyleId { get; set; }

    public List<int> SelectedPerkIds { get; set; } = [];

    public bool IsEditable { get; set; }

    public bool Current { get; set; }
}

/// <summary>A page the user saved for one champion, replayed the next time they pick it.</summary>
public sealed class RunePreset
{
    public int PrimaryStyleId { get; set; }

    public int SubStyleId { get; set; }

    public List<int> SelectedPerkIds { get; set; } = [];

    [JsonIgnore]
    public bool IsUsable => PrimaryStyleId > 0 && SubStyleId > 0 && SelectedPerkIds.Count > 0;
}

/// <summary>
/// Saves and replays rune pages through the client.
/// <para>
/// There is no recommendation source here on purpose. Scraping a build site would be fragile and
/// would put someone else's terms in the middle of a personal tool; instead the user sets a page up
/// the way they like it once and this records it for that champion.
/// </para>
/// <para>
/// Applying replaces the current page, which is destructive and cannot be undone from here — so it
/// only ever happens on an explicit request, or automatically if the user turns that on themselves.
/// </para>
/// </summary>
public sealed class RunePageService
{
    private const string PagesPath = "/lol-perks/v1/pages";
    private const string CurrentPagePath = "/lol-perks/v1/currentpage";

    /// <summary>Marks pages this tool created, so it never deletes one the user made by hand.</summary>
    public const string ManagedPageName = "RiftOverlay";

    private readonly LcuClient _client;
    private readonly ILog _log;

    public RunePageService(LcuClient client, ILog? log = null)
    {
        _client = client;
        _log = log ?? NullLog.Instance;
    }

    public async Task<RunePreset?> CaptureCurrentAsync(CancellationToken token = default)
    {
        var page = await _client.GetAsync<RunePage>(CurrentPagePath, token).ConfigureAwait(false);

        if (page is null || page.PrimaryStyleId == 0)
        {
            _log.Warn("Geçerli rün sayfası okunamadı.");
            return null;
        }

        return new RunePreset
        {
            PrimaryStyleId = page.PrimaryStyleId,
            SubStyleId = page.SubStyleId,
            SelectedPerkIds = [.. page.SelectedPerkIds],
        };
    }

    /// <summary>
    /// Writes the preset as the active page.
    /// <para>
    /// Only a page this tool created is deleted to make room. Accounts have very few rune page
    /// slots, so something has to give — but deleting a page the user built themselves to install
    /// one of ours would be indefensible, and there is no undo.
    /// </para>
    /// </summary>
    public async Task<bool> ApplyAsync(string championName, RunePreset preset, CancellationToken token = default)
    {
        if (!preset.IsUsable)
        {
            _log.Warn($"Rün ön ayarı eksik, uygulanmadı ({championName}).");
            return false;
        }

        var pages = await _client.GetAsync<List<RunePage>>(PagesPath, token).ConfigureAwait(false);

        if (pages is null)
        {
            return false;
        }

        foreach (var ours in pages.Where(p => p.IsEditable && p.Name.StartsWith(ManagedPageName, StringComparison.Ordinal)))
        {
            await _client.DeleteAsync($"{PagesPath}/{ours.Id}", token).ConfigureAwait(false);
        }

        var created = await _client.PostAsync(
            PagesPath,
            new
            {
                name = $"{ManagedPageName} · {championName}",
                primaryStyleId = preset.PrimaryStyleId,
                subStyleId = preset.SubStyleId,
                selectedPerkIds = preset.SelectedPerkIds,
                current = true,
            },
            token).ConfigureAwait(false);

        _log.Info(created
            ? $"Rün sayfası uygulandı: {championName}"
            : $"Rün sayfası uygulanamadı: {championName} — sayfa limiti dolu olabilir.");

        return created;
    }
}
