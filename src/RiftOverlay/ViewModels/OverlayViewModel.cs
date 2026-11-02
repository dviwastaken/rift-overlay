using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows.Media;
using RiftOverlay.Models;
using RiftOverlay.Services;
using RiftOverlay.Services.Lcu;

namespace RiftOverlay.ViewModels;

public sealed class OverlayViewModel : ObservableObject
{
    private const int FeedLength = 6;
    private const int EnemyItemLength = 4;

    private readonly ObjectiveTracker _tracker;
    private readonly CooldownTracker _cooldowns;
    private readonly BuildTargetTracker? _builds;
    private readonly ItemWatcher? _items;
    private IconCache? _icons;
    private readonly LaneMatchupResolver _lane = new();
    private readonly ILog _log;
    private readonly Stopwatch _sinceSnapshot = new();

    private GameSnapshot? _snapshot;
    private double _snapshotGameTime;
    private string _allyTeam = "ORDER";
    private string _statusText = "Maç bekleniyor…";
    private string _gameClock = "--:--";
    private bool _hasGame;
    private bool _isInteractive;
    private bool _hasWarnings;
    private bool _hasEnemyItems;

    public OverlayViewModel(
        ObjectiveSettings? settings = null,
        SummonerSpellSettings? spellSettings = null,
        OverlaySettings? overlaySettings = null,
        IItemCatalog? itemCatalog = null,
        ILog? log = null)
    {
        _tracker = new ObjectiveTracker(settings);
        _cooldowns = new CooldownTracker(spellSettings);
        _builds = itemCatalog is null ? null : new BuildTargetTracker(itemCatalog);
        _items = itemCatalog is null ? null : new ItemWatcher(itemCatalog);
        _log = log ?? NullLog.Instance;
        Settings = overlaySettings ?? new OverlaySettings();
    }

    public OverlaySettings Settings { get; }

    public BuildTargetItem Build { get; } = new();

    public LaneItem Lane { get; } = new();

    public ObservableCollection<LaneCandidateItem> LaneCandidates { get; } = [];

    public ObservableCollection<HotkeyItem> Hotkeys { get; } = [];


    /// <summary>Rebuilt whenever a binding changes so the card shows what is actually registered.</summary>
    public void RefreshHotkeys()
    {
        if (Hotkeys.Count == 0)
        {
            foreach (var (id, label) in HotkeyIds.Labels)
            {
                Hotkeys.Add(new HotkeyItem(id, label));
            }
        }

        foreach (var item in Hotkeys)
        {
            item.Display = Settings.HotkeyFor(item.Id);
        }
    }

    public void SetHotkeyCapturing(string? id)
    {
        foreach (var item in Hotkeys)
        {
            item.IsCapturing = item.Id == id;
        }

        if (id is null)
        {
            RefreshHotkeys();
        }
    }

    /// <summary>
    /// Things the overlay could not do and would otherwise fail at silently. Application-level,
    /// not per-match, so these deliberately survive a game ending.
    /// </summary>
    public ObservableCollection<string> Warnings { get; } = [];

    public bool HasWarnings
    {
        get => _hasWarnings;
        private set => Set(ref _hasWarnings, value);
    }

    public void AddWarning(string text)
    {
        if (string.IsNullOrWhiteSpace(text) || Warnings.Contains(text))
        {
            return;
        }

        Warnings.Add(text);
        HasWarnings = true;

        // Everything the panel shows also belongs in the file, so a problem the player dismissed
        // or never noticed is still there to read afterwards.
        _log.Warn(text);
    }

    /// <summary>
    /// Reports hotkeys Windows refused to hand over — almost always because another application
    /// already owns the combination. Without this the key simply does nothing and there is no way
    /// to tell that from a bug in the overlay.
    /// </summary>
    public void ReportFailedHotkeys(IReadOnlyList<string> failed)
    {
        if (failed.Count == 0)
        {
            return;
        }

        AddWarning($"Kısayol alınamadı ({string.Join(", ", failed)}) — başka bir uygulama kullanıyor.");
    }

    public ObservableCollection<BuildCandidateItem> BuildCandidates { get; } = [];

    /// <summary>Significant enemy items as they are completed, newest first.</summary>
    public ObservableCollection<EnemyItemItem> EnemyItems { get; } = [];

    /// <summary>Keeps the panel out of the way entirely until the first item is finished.</summary>
    public bool HasEnemyItems
    {
        get => _hasEnemyItems;
        private set => Set(ref _hasEnemyItems, value);
    }

    /// <summary>True while click-through is off, which is when the settings card is reachable.</summary>
    public bool IsInteractive
    {
        get => _isInteractive;
        set => Set(ref _isInteractive, value);
    }

    private double CurrentGameTime => _snapshotGameTime + _sinceSnapshot.Elapsed.TotalSeconds;

    public ObservableCollection<ObjectiveItem> Objectives { get; } = [];

    public ObservableCollection<RespawnItem> Enemies { get; } = [];

    public ObservableCollection<string> Feed { get; } = [];

    public string StatusText
    {
        get => _statusText;
        private set => Set(ref _statusText, value);
    }

    public string GameClock
    {
        get => _gameClock;
        private set => Set(ref _gameClock, value);
    }

    public bool HasGame
    {
        get => _hasGame;
        private set => Set(ref _hasGame, value);
    }

    public void OnStateChanged(DataSourceState state)
    {
        if (state == DataSourceState.Waiting)
        {
            HasGame = false;
            StatusText = "Maç bekleniyor…";
            _snapshot = null;
            _sinceSnapshot.Reset();
            _tracker.Reset();
            _cooldowns.Reset();
            Objectives.Clear();
            Enemies.Clear();
            Feed.Clear();
            BuildCandidates.Clear();
            LaneCandidates.Clear();
            EnemyItems.Clear();
            Lane.HasOpponent = false;
            _lane.ManualOpponent = null;
            HasEnemyItems = false;
            Build.HasTarget = false;
            _items?.Reset();

            if (_builds is not null)
            {
                _builds.ManualTargetId = null;
            }
        }
        else
        {
            StatusText = "Bağlı";
        }
    }

    public void OnSnapshot(GameSnapshot snapshot)
    {
        _snapshot = snapshot;
        _snapshotGameTime = snapshot.GameData.GameTime;
        _sinceSnapshot.Restart();

        _tracker.Process(snapshot);
        _allyTeam = ResolveAllyTeam(snapshot);
        _tracker.AllyTeam = _allyTeam;
        _tracker.MapName = snapshot.GameData.MapName;

        // Naming the mode explains the missing counters: outside the Rift there is no dragon to
        // count down to, and a silently shorter row would just look broken.
        var mode = DescribeMode(snapshot.GameData);
        StatusText = mode is null ? "Bağlı" : $"Bağlı · {mode}";

        SyncEnemies(snapshot);
        SyncFeed(snapshot);
        // Gold and inventories only move when a snapshot arrives, so neither belongs in the
        // 100 ms tick.
        SyncBuild(snapshot);
        SyncEnemyItems(snapshot);
        SyncLane(snapshot);

        HasGame = true;
        Tick();
    }

    private void SyncBuild(GameSnapshot snapshot)
    {
        if (_builds is null)
        {
            Build.HasTarget = false;
            return;
        }

        var self = ResolveSelf(snapshot);

        if (self is null)
        {
            Build.HasTarget = false;
            BuildCandidates.Clear();
            return;
        }

        SyncBuildCandidates(self);

        var target = _builds.Resolve(self.Items, snapshot.ActivePlayer?.CurrentGold ?? 0);

        if (target is null)
        {
            Build.HasTarget = false;
            return;
        }

        Build.HasTarget = true;
        Build.ItemName = target.Item.Name + (target.IsAmbiguous ? " ?" : string.Empty);
        LoadIcon(target.Item.Id, image => Build.Icon = image);
        Build.IsAffordable = target.Remaining == 0;
        Build.Remaining = target.Remaining == 0 ? "alabilirsin" : $"{target.Remaining}";
        Build.Completion = target.Completion;
        Build.Detail = target.IsManual
            ? $"{target.Item.TotalGold} altın · elle seçildi"
            : $"{target.Item.TotalGold} altın";
    }

    /// <summary>
    /// Watches only the enemy team: your own completions are not news, and a teammate finishing an
    /// item is not something you act on.
    /// </summary>
    private void SyncEnemyItems(GameSnapshot snapshot)
    {
        if (_items is null)
        {
            return;
        }

        var enemies = snapshot.AllPlayers
            .Where(p => !string.Equals(p.Team, _allyTeam, StringComparison.OrdinalIgnoreCase));

        foreach (var completion in _items.Observe(enemies, snapshot.GameData.GameTime))
        {
            var row = new EnemyItemItem(
                FormatElapsed(completion.GameTime), completion.Champion, completion.ItemId, completion.ItemName);

            EnemyItems.Insert(0, row);
            LoadIcon(completion.ItemId, image => row.Icon = image);

            _log.Info($"Rakip eşya: {completion.Champion} → {completion.ItemName} ({completion.Cost})");
        }

        while (EnemyItems.Count > EnemyItemLength)
        {
            EnemyItems.RemoveAt(EnemyItems.Count - 1);
        }

        // Only the newest line is highlighted, so the eye lands on what just changed.
        for (var i = 0; i < EnemyItems.Count; i++)
        {
            EnemyItems[i].IsLatest = i == 0;
        }

        HasEnemyItems = EnemyItems.Count > 0;
    }

    private void SyncLane(GameSnapshot snapshot)
    {
        var self = ResolveSelf(snapshot);
        var enemies = snapshot.AllPlayers
            .Where(p => !string.Equals(p.Team, _allyTeam, StringComparison.OrdinalIgnoreCase))
            .ToList();

        SyncLaneCandidates(enemies);

        var comparison = _lane.Resolve(self, enemies);

        if (comparison is null)
        {
            Lane.HasOpponent = false;
            return;
        }

        Lane.HasOpponent = true;
        Lane.Opponent = comparison.Opponent.ChampionName;
        Lane.Detail = comparison.IsManual ? "elle seçildi" : comparison.Opponent.Position.ToLowerInvariant();

        Lane.SelfCreepScore = comparison.SelfCreepScore.ToString();
        Lane.OpponentCreepScore = comparison.OpponentCreepScore.ToString();
        Lane.CreepScoreDifference = Signed(comparison.CreepScoreDifference);
        Lane.CreepScoreLead = comparison.CreepScoreDifference;

        Lane.SelfLevel = comparison.SelfLevel.ToString();
        Lane.OpponentLevel = comparison.OpponentLevel.ToString();
        Lane.LevelDifference = Signed(comparison.LevelDifference);
        Lane.LevelLead = comparison.LevelDifference;
    }

    private void SyncLaneCandidates(IReadOnlyList<PlayerState> enemies)
    {
        var wanted = new List<LaneCandidateItem> { new(null, "Otomatik") };
        wanted.AddRange(enemies.Select(e => new LaneCandidateItem(e.SummonerName, e.ChampionName)));

        if (!LaneCandidates.Select(c => c.Player).SequenceEqual(wanted.Select(c => c.Player)))
        {
            LaneCandidates.Clear();
            foreach (var candidate in wanted)
            {
                LaneCandidates.Add(candidate);
            }
        }

        foreach (var candidate in LaneCandidates)
        {
            candidate.IsSelected = candidate.Player == _lane.ManualOpponent;
        }
    }

    /// <summary>Called from the picker; null goes back to matching on position.</summary>
    public void SelectLaneOpponent(string? summonerName)
    {
        _lane.ManualOpponent = summonerName;

        if (_snapshot is not null)
        {
            SyncLane(_snapshot);
        }
    }

    private static string Signed(int difference) => difference == 0 ? "=" : $"{difference:+#;-#}";

    private void SyncBuildCandidates(PlayerState self)
    {
        var candidates = _builds!.Candidates(self.Items);

        var wanted = new List<BuildCandidateItem> { new(null, "Otomatik") };
        wanted.AddRange(candidates.Select(c => new BuildCandidateItem(c.Id, c.Name)));

        if (!BuildCandidates.Select(c => c.ItemId).SequenceEqual(wanted.Select(c => c.ItemId)))
        {
            BuildCandidates.Clear();
            foreach (var candidate in wanted)
            {
                BuildCandidates.Add(candidate);
            }
        }

        foreach (var candidate in BuildCandidates)
        {
            candidate.IsSelected = candidate.ItemId == _builds.ManualTargetId;
        }
    }

    /// <summary>Called from the picker in interaction mode; null goes back to inferring.</summary>
    public void SelectBuildTarget(int? itemId)
    {
        if (_builds is null)
        {
            return;
        }

        _builds.ManualTargetId = itemId;

        if (_snapshot is not null)
        {
            SyncBuild(_snapshot);
        }
    }

    /// <summary>
    /// Interpolates between polls so countdowns move smoothly instead of stepping every 250 ms.
    /// </summary>
    public void Tick()
    {
        if (_snapshot is null)
        {
            return;
        }

        var elapsed = _sinceSnapshot.Elapsed.TotalSeconds;
        var gameTime = _snapshotGameTime + elapsed;

        GameClock = FormatElapsed(gameTime);

        SyncObjectives(gameTime);

        foreach (var enemy in Enemies)
        {
            var player = _snapshot.AllPlayers.FirstOrDefault(p => p.SummonerName == enemy.Player);
            if (player is null)
            {
                continue;
            }

            var remaining = Math.Max(0, player.RespawnTimer - elapsed);

            enemy.Level = player.Level;
            enemy.Kda = $"{player.Scores.Kills}/{player.Scores.Deaths}/{player.Scores.Assists}";
            enemy.IsDead = remaining > 0;
            enemy.Remaining = remaining > 0 ? FormatCountdown(remaining) : "canlı";

            UpdateSpell(enemy.SpellOne, player, SpellSlot.First, gameTime);
            UpdateSpell(enemy.SpellTwo, player, SpellSlot.Second, gameTime);
        }
    }

    /// <summary>
    /// Starts (or cancels) the countdown for the enemy's Flash — or their first spell if they did
    /// not take Flash. Bound to a global hotkey, since the game never reports enemy cooldowns.
    /// </summary>
    public void MarkFlash(int enemyIndex) => MarkSpell(enemyIndex, flash: true);

    public void MarkOtherSpell(int enemyIndex) => MarkSpell(enemyIndex, flash: false);

    private void MarkSpell(int enemyIndex, bool flash)
    {
        if (_snapshot is null || enemyIndex < 0 || enemyIndex >= Enemies.Count)
        {
            return;
        }

        var player = _snapshot.AllPlayers.FirstOrDefault(p => p.SummonerName == Enemies[enemyIndex].Player);
        if (player is null)
        {
            return;
        }

        var flashSlot = string.Equals(
            CooldownTracker.SpellName(player, SpellSlot.Second), "Flash", StringComparison.OrdinalIgnoreCase)
            ? SpellSlot.Second
            : SpellSlot.First;

        var slot = flash
            ? flashSlot
            : flashSlot == SpellSlot.First ? SpellSlot.Second : SpellSlot.First;

        _cooldowns.Toggle(player, slot, CurrentGameTime);
        Tick();
    }

    private void UpdateSpell(SpellItem item, PlayerState player, SpellSlot slot, double gameTime)
    {
        item.Name = CooldownTracker.SpellName(player, slot);

        var cooldown = _cooldowns.Get(player.SummonerName, slot, gameTime);

        item.IsReady = cooldown is null;
        item.Remaining = cooldown is null
            ? string.Empty
            // "~" marks a lower bound: Cosmic Insight is invisible to us, so the timer assumes it.
            : FormatCountdown(cooldown.ReadyAtGameTime - gameTime) + (cooldown.IsUncertain ? "~" : string.Empty);
    }

    private void SyncObjectives(double gameTime)
    {
        var states = _tracker.GetObjectives(gameTime)
            .Where(state => Settings.IsVisible(state.Kind))
            .ToList();

        if (Objectives.Count != states.Count ||
            !Objectives.Select(o => o.Kind).SequenceEqual(states.Select(s => s.Kind)))
        {
            Objectives.Clear();
            foreach (var state in states)
            {
                Objectives.Add(new ObjectiveItem(state.Kind, state.Label));
            }
        }

        for (var i = 0; i < states.Count; i++)
        {
            var state = states[i];
            var item = Objectives[i];
            var remaining = state.SpawnsAtGameTime - gameTime;

            item.Detail = state.Detail;
            item.IsReady = remaining <= 0;
            item.IsImminent = remaining > 0 && remaining <= ObjectiveItem.ImminentSeconds;
            item.Remaining = remaining <= 0 ? "hazır" : FormatCountdown(remaining);
        }
    }

    private void SyncEnemies(GameSnapshot snapshot)
    {
        var enemies = snapshot.AllPlayers
            .Where(p => !string.Equals(p.Team, _allyTeam, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (Enemies.Count == enemies.Count &&
            Enemies.Select(e => e.Player).SequenceEqual(enemies.Select(p => p.SummonerName)))
        {
            return;
        }

        Enemies.Clear();
        foreach (var enemy in enemies)
        {
            Enemies.Add(new RespawnItem(enemy.ChampionName, enemy.SummonerName));
        }
    }

    private void SyncFeed(GameSnapshot snapshot)
    {
        var recent = snapshot.Events.Events
            .OrderByDescending(e => e.EventId)
            .Take(FeedLength)
            .Select(Describe)
            .ToList();

        if (Feed.SequenceEqual(recent))
        {
            return;
        }

        Feed.Clear();
        foreach (var line in recent)
        {
            Feed.Add(line);
        }
    }

    private static string Describe(GameEvent gameEvent)
    {
        var clock = FormatElapsed(gameEvent.EventTime);

        var text = gameEvent.EventName switch
        {
            "ChampionKill" => $"{Short(gameEvent.KillerName)} → {Short(gameEvent.VictimName)}",
            "DragonKill" => $"Dragon {gameEvent.DragonType} — {Short(gameEvent.KillerName)}",
            "BaronKill" => $"Baron — {Short(gameEvent.KillerName)}",
            "HeraldKill" => $"Herald — {Short(gameEvent.KillerName)}",
            "HordeKill" => $"Void grub — {Short(gameEvent.KillerName)}",
            "TurretKilled" => "Kule düştü",
            "FirstBrick" => $"İlk kule — {Short(gameEvent.KillerName)}",
            "InhibKilled" => "Inhibitor düştü",
            "InhibRespawningSoon" => "Inhibitor birazdan dönüyor",
            "InhibRespawned" => "Inhibitor döndü",
            "FirstBlood" => $"İlk kan — {Short(gameEvent.Recipient)}",
            "Multikill" => $"{Short(gameEvent.KillerName)} {Multikill(gameEvent.KillStreak)}",
            "Ace" => $"Ace — {Short(gameEvent.Acer)}",
            "GameStart" => "Oyun başladı",
            "GameEnd" => "Oyun bitti",
            "MinionsSpawning" => "Minyonlar çıktı",
            _ => gameEvent.EventName,
        };

        return $"{clock}  {text}";
    }

    private static string Short(string? riotId) => PlayerIdentity.Short(riotId);

    private static string Multikill(int killStreak) => killStreak switch
    {
        2 => "double kill",
        3 => "triple kill",
        4 => "quadra kill",
        >= 5 => "penta kill",
        _ => "multikill",
    };

    /// <returns>A short label for anything that is not a normal Rift game, otherwise null.</returns>
    private static string? DescribeMode(GameStats game)
    {
        // The mode is the more specific fact and has to win: Riot runs limited modes on the
        // Howling Abyss that are not ARAM, and deriving the label from the map alone mislabels
        // them. An unrecognised code is shown verbatim rather than guessed at.
        if (!string.IsNullOrWhiteSpace(game.GameMode) &&
            !string.Equals(game.GameMode, "CLASSIC", StringComparison.OrdinalIgnoreCase))
        {
            return game.GameMode.ToUpperInvariant() switch
            {
                "ARAM" => "ARAM",
                "CHERRY" => "Arena",
                "URF" or "ARURF" => "URF",
                "ONEFORTHEALL" => "One for All",
                "NEXUSBLITZ" => "Nexus Blitz",
                "PRACTICETOOL" => "Pratik",
                "TUTORIAL" => "Eğitim",
                _ => game.GameMode,
            };
        }

        return game.MapName switch
        {
            ObjectiveTracker.SummonersRift or "" or null => null,
            "Map12" => "Howling Abyss",
            "Map21" => "Nexus Blitz",
            "Map30" => "Arena",
            _ => game.MapName,
        };
    }

    private static string ResolveAllyTeam(GameSnapshot snapshot) => ResolveSelf(snapshot)?.Team ?? "ORDER";

    private static PlayerState? ResolveSelf(GameSnapshot snapshot) =>
        PlayerIdentity.ResolveSelf(snapshot);

    /// <summary>
    /// Gives the view model somewhere to fetch artwork from. Optional on purpose: without it every
    /// panel still works, just without pictures.
    /// </summary>
    public void AttachIcons(IconCache icons) => _icons = icons;

    /// <summary>
    /// Loads an icon in the background and hands it to <paramref name="apply"/> when it arrives.
    /// <para>
    /// Fire-and-forget rather than awaited: this runs inside the snapshot handler, which is on the
    /// UI thread and runs four times a second. Awaiting a download there would stall the overlay
    /// for as long as the CDN takes to answer — the one thing a live overlay cannot afford.
    /// </para>
    /// </summary>
    private async void LoadIcon(int itemId, Action<ImageSource?> apply)
    {
        if (_icons is null || itemId <= 0)
        {
            return;
        }

        try
        {
            // The continuation returns to the UI thread, so the assignment is safe.
            var image = await _icons.GetItemIconAsync(itemId).ConfigureAwait(true);

            if (image is not null)
            {
                apply(image);
            }
        }
        catch (Exception exception)
        {
            // An async void method that throws takes the process down. Artwork is never worth that.
            _log.Warn($"İkon yüklenemedi ({itemId}): {exception.Message}");
        }
    }

    /// <summary>Elapsed time, floored — matches the clock the game itself shows.</summary>
    private static string FormatElapsed(double seconds) => Format((int)Math.Max(0, seconds));

    /// <summary>
    /// Remaining time, rounded up. Flooring would show "00:27" the instant a 28 second respawn
    /// starts, leaving every countdown a second short of the truth.
    /// </summary>
    private static string FormatCountdown(double seconds) => Format((int)Math.Ceiling(Math.Max(0, seconds)));

    private static string Format(int seconds) => $"{seconds / 60:00}:{seconds % 60:00}";
}
