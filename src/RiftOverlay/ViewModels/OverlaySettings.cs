using System.Text.Json.Serialization;
using RiftOverlay.Services;
using RiftOverlay.Services.Lcu;

namespace RiftOverlay.ViewModels;

/// <summary>
/// What the overlay draws and how strongly. Persisted by <see cref="SettingsService"/> and bound
/// straight into the settings card, so every toggle takes effect the moment it is clicked.
/// </summary>
public sealed class OverlaySettings : ObservableObject
{
    private bool _showStatus = true;
    private bool _showObjectives = true;
    private bool _showEnemies = true;
    private bool _showFeed = true;
    private bool _showBuild = true;
    private bool _showEnemyItems = true;
    private bool _showLane = true;
    private bool _autoApplyRunes;
    private bool _showDragon = true;
    private bool _showHerald = true;
    private bool _showBaron = true;
    private bool _showElder = true;
    private bool _showBuffs = true;
    private bool _showInhibitors = true;
    private bool _showVoidgrubs = true;
    private bool _showWave;
    private double _opacity = 1;
    private string _itemLocale = "tr_TR";

    public bool ShowStatus
    {
        get => _showStatus;
        set => Set(ref _showStatus, value);
    }

    public bool ShowObjectives
    {
        get => _showObjectives;
        set => Set(ref _showObjectives, value);
    }

    public bool ShowEnemies
    {
        get => _showEnemies;
        set => Set(ref _showEnemies, value);
    }

    public bool ShowFeed
    {
        get => _showFeed;
        set => Set(ref _showFeed, value);
    }

    public bool ShowBuild
    {
        get => _showBuild;
        set => Set(ref _showBuild, value);
    }

    public bool ShowEnemyItems
    {
        get => _showEnemyItems;
        set => Set(ref _showEnemyItems, value);
    }

    public bool ShowLane
    {
        get => _showLane;
        set => Set(ref _showLane, value);
    }

    /// <summary>
    /// Off by default. Applying a preset deletes and rewrites a rune page, which cannot be undone
    /// from here, so it stays a deliberate choice rather than something that happens the first time
    /// the user picks a champion they once saved runes for.
    /// </summary>
    public bool AutoApplyRunes
    {
        get => _autoApplyRunes;
        set => Set(ref _autoApplyRunes, value);
    }

    /// <summary>Saved rune pages by champion name.</summary>
    public Dictionary<string, RunePreset> RunePresets { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public RunePreset? PresetFor(string champion) =>
        RunePresets.TryGetValue(champion, out var preset) && preset.IsUsable ? preset : null;

    public void SetPreset(string champion, RunePreset preset)
    {
        RunePresets[champion] = preset;
        Raise(nameof(RunePresets));
    }

    public bool ShowDragon
    {
        get => _showDragon;
        set => Set(ref _showDragon, value);
    }

    public bool ShowHerald
    {
        get => _showHerald;
        set => Set(ref _showHerald, value);
    }

    public bool ShowBaron
    {
        get => _showBaron;
        set => Set(ref _showBaron, value);
    }

    public bool ShowElder
    {
        get => _showElder;
        set => Set(ref _showElder, value);
    }

    public bool ShowBuffs
    {
        get => _showBuffs;
        set => Set(ref _showBuffs, value);
    }

    public bool ShowInhibitors
    {
        get => _showInhibitors;
        set => Set(ref _showInhibitors, value);
    }

    public bool ShowVoidgrubs
    {
        get => _showVoidgrubs;
        set => Set(ref _showVoidgrubs, value);
    }

    /// <summary>
    /// Off by default: unlike the others this counter is never idle, so leaving it on would add a
    /// permanent chip to the row for something most players do not track every game.
    /// </summary>
    public bool ShowWave
    {
        get => _showWave;
        set => Set(ref _showWave, value);
    }

    /// <summary>Clamped on load so a hand-edited file cannot make the overlay invisible.</summary>
    public double Opacity
    {
        get => _opacity;
        set => Set(ref _opacity, Math.Clamp(value, 0.3, 1));
    }

    /// <summary>
    /// Data Dragon locale for item names. The Live Client API reports names in the client's own
    /// language, so this has to match the game or the build panel names an item the player is not
    /// looking at. Falls back to en_US if the CDN has no such locale.
    /// </summary>
    public string ItemLocale
    {
        get => _itemLocale;
        set => Set(ref _itemLocale, string.IsNullOrWhiteSpace(value) ? "tr_TR" : value);
    }

    /// <summary>
    /// Panel positions by id, only for panels the user has actually moved — anything absent falls
    /// back to <see cref="PanelIds.Defaults"/>, so a new panel appears where it was designed to
    /// rather than at the origin.
    /// </summary>
    public Dictionary<string, PanelPlacement> Panels { get; set; } = new(StringComparer.Ordinal);

    public PanelPlacement PlacementFor(string id) =>
        Panels.TryGetValue(id, out var placement) ? placement : PanelIds.DefaultFor(id);

    /// <summary>
    /// A dictionary mutation raises no property change of its own, so persistence has to be
    /// triggered explicitly rather than relying on the setter.
    /// </summary>
    public void SetPlacement(string id, double x, double y)
    {
        Panels[id] = new PanelPlacement { X = x, Y = y };
        Raise(nameof(Panels));
    }

    public void ResetPlacements()
    {
        if (Panels.Count == 0)
        {
            return;
        }

        Panels.Clear();
        Raise(nameof(Panels));
    }

    /// <summary>
    /// Key bindings by id, in the same text the settings card shows. Only entries the user has
    /// changed are stored; the rest come from <see cref="HotkeyIds.Defaults"/>.
    /// </summary>
    public Dictionary<string, string> Hotkeys { get; set; } = new(StringComparer.Ordinal);

    public string HotkeyFor(string id) =>
        Hotkeys.TryGetValue(id, out var text) && !string.IsNullOrWhiteSpace(text)
            ? text
            : HotkeyIds.DefaultFor(id);

    public void SetHotkey(string id, string display)
    {
        Hotkeys[id] = display;
        Raise(nameof(Hotkeys));
    }

    public void ResetHotkeys()
    {
        if (Hotkeys.Count == 0)
        {
            return;
        }

        Hotkeys.Clear();
        Raise(nameof(Hotkeys));
    }

    [JsonIgnore]
    public bool AnyObjectiveVisible => _showDragon || _showHerald || _showBaron || _showElder;

    public bool IsVisible(ObjectiveKind kind) => kind switch
    {
        ObjectiveKind.Dragon => ShowDragon,
        ObjectiveKind.Elder => ShowElder,
        ObjectiveKind.Baron => ShowBaron,
        ObjectiveKind.Herald => ShowHerald,
        ObjectiveKind.BaronBuff or ObjectiveKind.ElderBuff => ShowBuffs,
        ObjectiveKind.Inhibitor => ShowInhibitors,
        ObjectiveKind.Voidgrubs => ShowVoidgrubs,
        ObjectiveKind.Wave => ShowWave,
        _ => true,
    };
}
