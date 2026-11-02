using System.Collections.ObjectModel;
using System.Windows.Media;
using RiftOverlay.Services;
using RiftOverlay.Services.Lcu;
using RiftOverlay.ViewModels;

namespace RiftOverlay.Client;

/// <summary>
/// One rendered row.
/// <para>
/// The portrait is part of the value rather than a property filled in later, and that is what keeps
/// <see cref="ChampSelectViewModel"/>'s once-a-second comparison working: a row whose artwork has
/// arrived is simply a different row, so the list rebuilds once when the picture lands instead of
/// either rebuilding every tick or never noticing.
/// </para>
/// </summary>
public sealed record PickLine(
    string Champion,
    string Position,
    string State,
    bool IsSelf,
    bool IsLocked,
    ImageSource? Portrait);

/// <summary>
/// Champion select, shown in the client rather than over the game.
/// <para>
/// It lives here because none of it happens during a match. Drawing it as a click-through overlay
/// meant cramming two teams and a ban list into a corner of a window that exists to stay out of the
/// way — while the client, which is on screen at exactly that moment and has room, showed nothing.
/// </para>
/// </summary>
public sealed class ChampSelectViewModel : ObservableObject
{
    private readonly RunePageService? _runes;
    private readonly OverlaySettings? _settings;
    private readonly IconCache? _icons;
    private readonly ILog _log;

    private string _phase = "None";
    private string _selfChampion = string.Empty;
    private string _presetLabel = string.Empty;
    private string _status = "İstemci bekleniyor…";
    private string _clock = string.Empty;
    private string _bans = string.Empty;
    private bool _inChampSelect;

    public ChampSelectViewModel(
        RunePageService? runes = null,
        OverlaySettings? settings = null,
        IconCache? icons = null,
        ILog? log = null)
    {
        _runes = runes;
        _settings = settings;
        _icons = icons;
        _log = log ?? NullLog.Instance;
    }

    public ObservableCollection<PickLine> Allies { get; } = [];

    public ObservableCollection<PickLine> Enemies { get; } = [];

    public string Status
    {
        get => _status;
        private set => Set(ref _status, value);
    }

    /// <summary>Time left in the current phase; blank outside champion select.</summary>
    public string Clock
    {
        get => _clock;
        private set => Set(ref _clock, value);
    }

    public string Bans
    {
        get => _bans;
        private set => Set(ref _bans, value);
    }

    public bool InChampSelect
    {
        get => _inChampSelect;
        private set
        {
            if (Set(ref _inChampSelect, value))
            {
                Raise(nameof(HasBans));
            }
        }
    }

    public bool HasBans => _inChampSelect && _bans.Length > 0;

    /// <summary>
    /// Whether saving a rune page is possible right now. Only during champion select and only with
    /// a champion picked — there is nothing to attach a page to otherwise.
    /// </summary>
    public bool CanSaveRunes =>
        _runes is not null && _settings is not null && _inChampSelect &&
        !string.IsNullOrWhiteSpace(_selfChampion) && _selfChampion != "—";

    /// <summary>Says whether this champion already has a page saved, before anything is written.</summary>
    public string PresetLabel
    {
        get => _presetLabel;
        private set => Set(ref _presetLabel, value);
    }

    /// <summary>
    /// Stores the page the user has set up for the champion they picked, to be replayed later.
    /// <para>
    /// Reads only — nothing is written to the account here. The write happens later, when the
    /// preset is applied, and only over pages this tool created.
    /// </para>
    /// </summary>
    public async Task SaveRunesAsync()
    {
        if (!CanSaveRunes)
        {
            return;
        }

        try
        {
            var preset = await _runes!.CaptureCurrentAsync().ConfigureAwait(true);

            if (preset is null || !preset.IsUsable)
            {
                PresetLabel = "Geçerli rün sayfası okunamadı.";
                return;
            }

            _settings!.SetPreset(_selfChampion, preset);
            RefreshPresetLabel();
        }
        catch (Exception exception)
        {
            _log.Error("Rün ön ayarı kaydedilemedi", exception);
            PresetLabel = "Rün sayfası kaydedilemedi.";
        }
    }

    private void RefreshPresetLabel()
    {
        if (_settings is null || !CanSaveRunes)
        {
            PresetLabel = string.Empty;
            return;
        }

        PresetLabel = _settings.PresetFor(_selfChampion) is null
            ? $"{_selfChampion}: kayıtlı rün yok"
            : $"{_selfChampion}: kayıtlı ✓";
    }

    /// <summary>
    /// Reflects where the client is when there is no champion select to show. Without it the
    /// section would be blank in a lobby and blank in a queue and blank when the client is closed,
    /// three quite different situations.
    /// </summary>
    public void OnPhase(string phase)
    {
        _phase = phase;

        if (InChampSelect)
        {
            return;
        }

        Status = phase switch
        {
            "Lobby" => "Lobide.",
            "Matchmaking" => "Sıra bekleniyor…",
            "ReadyCheck" => "Maç bulundu — kabul et.",
            "ChampSelect" => "Şampiyon seçimi başlıyor…",
            "InProgress" => "Maç sürüyor. Oyun içi bilgiler overlay'de.",
            "None" => "İstemci kapalı.",
            _ => phase,
        };
    }

    public void OnState(ChampSelectState? state)
    {
        if (state is null)
        {
            InChampSelect = false;
            _selfChampion = string.Empty;
            Allies.Clear();
            Enemies.Clear();
            Clock = string.Empty;
            Bans = string.Empty;

            RefreshPresetLabel();
            Raise(nameof(CanSaveRunes));
            OnPhase(_phase);
            return;
        }

        InChampSelect = true;
        Clock = Format(state.SecondsLeft);
        Bans = state.EnemyBans.Count == 0 ? string.Empty : string.Join(", ", state.EnemyBans);

        Fill(Allies, state.Allies);
        Fill(Enemies, state.Enemies);

        Status = state.SelfLocked
            ? $"{state.SelfChampion} kilitlendi."
            : state.SelfChampion is { Length: > 0 } and not "—"
                ? $"{state.SelfChampion} seçiliyor…"
                : "Şampiyon seçimi.";

        _selfChampion = state.SelfChampion;

        RefreshPresetLabel();
        Raise(nameof(HasBans));
        Raise(nameof(CanSaveRunes));
    }

    /// <summary>
    /// Rewrites the list in place only when it actually changed. Champion select is polled once a
    /// second and replacing the collection every tick would restart selection and scrolling under
    /// the user's hands.
    /// </summary>
    private void Fill(ObservableCollection<PickLine> target, IReadOnlyList<ChampSelectPick> picks)
    {
        var lines = picks.Select(ToLine).ToList();

        if (target.Count == lines.Count &&
            target.Zip(lines).All(pair => pair.First == pair.Second))
        {
            return;
        }

        target.Clear();

        foreach (var line in lines)
        {
            target.Add(line);
        }
    }

    private PickLine ToLine(ChampSelectPick pick) => new(
        pick.Champion,
        Position(pick.Position),
        // Hovering and locked are different things and the row says which: a hovered pick can still
        // change, and treating the two the same is how a plan gets made against a champion nobody
        // ended up playing.
        pick.IsLocked ? "kilitli" : pick.Champion == "—" ? string.Empty : "seçiyor",
        pick.IsSelf,
        pick.IsLocked,
        Portrait(pick.ChampionId));

    /// <summary>
    /// The portrait if it is already in hand, and nothing otherwise. Asking does start the fetch,
    /// so the picture appears on the next poll a second later — invisible next to a draft that runs
    /// for minutes, and far simpler than making the row mutable.
    /// </summary>
    private ImageSource? Portrait(int championId)
    {
        if (_icons is null || championId <= 0)
        {
            return null;
        }

        _icons.TryGetChampionIcon(championId, out var icon);

        return icon;
    }

    /// <summary>
    /// Custom games leave the position empty for everyone, so a blank is expected rather than a
    /// fault — it simply shows nothing instead of a placeholder.
    /// </summary>
    private static string Position(string position) => position.ToUpperInvariant() switch
    {
        "TOP" => "üst",
        "JUNGLE" => "orman",
        "MIDDLE" or "MID" => "orta",
        "BOTTOM" or "ADC" => "alt",
        "UTILITY" or "SUPPORT" => "destek",
        "NONE" or "" => string.Empty,
        _ => position.ToLowerInvariant(),
    };

    private static string Format(int seconds) => $"{seconds / 60}:{seconds % 60:00}";
}
