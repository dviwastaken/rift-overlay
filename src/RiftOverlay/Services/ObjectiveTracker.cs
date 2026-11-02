using RiftOverlay.Models;

namespace RiftOverlay.Services;

public enum ObjectiveKind
{
    Dragon,
    Elder,
    Baron,
    Herald,
    BaronBuff,
    ElderBuff,
    Inhibitor,
    Voidgrubs,
    Wave,
}

public sealed record ObjectiveState(ObjectiveKind Kind, string Label, string Detail, double SpawnsAtGameTime);

/// <summary>
/// Spawn, respawn and duration timings, in seconds of game time. Riot moves these around between
/// patches (and per game mode), so treat them as configuration and re-check them against the live
/// patch notes rather than as constants that stay true forever.
/// </summary>
public sealed class ObjectiveSettings
{
    public double DragonFirstSpawn { get; init; } = 5 * 60;

    public double DragonRespawn { get; init; } = 5 * 60;

    public double ElderRespawn { get; init; } = 6 * 60;

    public double BaronFirstSpawn { get; init; } = 20 * 60;

    public double BaronRespawn { get; init; } = 6 * 60;

    public double HeraldFirstSpawn { get; init; } = 14 * 60;

    public double HeraldRespawn { get; init; } = 4 * 60;

    /// <summary>Herald stops spawning once Baron takes over the pit.</summary>
    public double HeraldDespawn { get; init; } = 20 * 60;

    public double BaronBuffDuration { get; init; } = 3 * 60;

    public double ElderBuffDuration { get; init; } = 150;

    public double InhibitorRespawn { get; init; } = 5 * 60;

    public double VoidgrubsFirstSpawn { get; init; } = 6 * 60;

    public double VoidgrubsDespawn { get; init; } = 14 * 60;

    public double FirstWave { get; init; } = 65;

    public double WaveInterval { get; init; } = 30;

    /// <summary>Cannon minion cadence before and after <see cref="CannonCadenceChangesAt"/>.</summary>
    public int CannonEveryNthWave { get; init; } = 3;

    public int CannonEveryNthWaveLate { get; init; } = 2;

    public double CannonCadenceChangesAt { get; init; } = 15 * 60;
}

/// <summary>
/// Derives every countdown in the objective row from the live event feed. Respawns and buff
/// durations are anchored to observed kill events, so those are exact; opening spawns and the
/// minion cadence come from <see cref="ObjectiveSettings"/> and are only as accurate as the
/// configured patch values.
/// </summary>
public sealed partial class ObjectiveTracker
{
    private readonly ObjectiveSettings _settings;

    private int _lastEventId = -1;
    private double _dragonSpawnsAt;
    private double _baronSpawnsAt;
    private double _heraldSpawnsAt;
    private double? _elderSpawnsAt;
    private double? _baronBuffEndsAt;
    private double? _elderBuffEndsAt;
    private string _baronBuffTeam = string.Empty;
    private string _elderBuffTeam = string.Empty;
    private string _lastDragonType = string.Empty;

    private readonly Dictionary<string, double> _inhibitorsDown = new(StringComparer.OrdinalIgnoreCase);

    private readonly Dictionary<string, int> _dragonsByTeam = new(StringComparer.OrdinalIgnoreCase)
    {
        ["ORDER"] = 0,
        ["CHAOS"] = 0,
    };

    private readonly Dictionary<string, int> _voidgrubsByTeam = new(StringComparer.OrdinalIgnoreCase)
    {
        ["ORDER"] = 0,
        ["CHAOS"] = 0,
    };

    public ObjectiveTracker(ObjectiveSettings? settings = null)
    {
        _settings = settings ?? new ObjectiveSettings();
        Reset();
    }

    /// <summary>Set by the view model so buffs and structures can be labelled "bizde" / "rakipte".</summary>
    public string AllyTeam { get; set; } = "ORDER";

    /// <summary>
    /// Map the current game is on, straight from <c>gameData.mapName</c>. An <em>empty</em> value
    /// is treated as the Rift — missing data is not evidence of a different mode — but an
    /// unrecognised map id is not, since a new id is far more likely to be a new mode than a new
    /// Rift, and inventing a Baron timer there is the very bug this guards against.
    /// </summary>
    public string MapName { get; set; } = SummonersRift;

    public const string SummonersRift = "Map11";

    /// <summary>
    /// Dragons, Baron, Herald and void grubs only exist on the Rift; showing their countdowns in
    /// ARAM or Arena would be inventing objectives that are not in the game.
    /// </summary>
    private bool IsSummonersRift =>
        string.IsNullOrWhiteSpace(MapName) ||
        string.Equals(MapName, SummonersRift, StringComparison.OrdinalIgnoreCase);

    public int OrderDragons => _dragonsByTeam["ORDER"];

    public int ChaosDragons => _dragonsByTeam["CHAOS"];

    public int OrderVoidgrubs => _voidgrubsByTeam["ORDER"];

    public int ChaosVoidgrubs => _voidgrubsByTeam["CHAOS"];

    public void Reset()
    {
        _lastEventId = -1;
        _dragonSpawnsAt = _settings.DragonFirstSpawn;
        _baronSpawnsAt = _settings.BaronFirstSpawn;
        _heraldSpawnsAt = _settings.HeraldFirstSpawn;
        _elderSpawnsAt = null;
        _baronBuffEndsAt = null;
        _elderBuffEndsAt = null;
        _baronBuffTeam = string.Empty;
        _elderBuffTeam = string.Empty;
        _lastDragonType = string.Empty;
        _inhibitorsDown.Clear();
        _dragonsByTeam["ORDER"] = 0;
        _dragonsByTeam["CHAOS"] = 0;
        _voidgrubsByTeam["ORDER"] = 0;
        _voidgrubsByTeam["CHAOS"] = 0;
    }

    public void Process(GameSnapshot snapshot)
    {
        foreach (var gameEvent in snapshot.Events.Events)
        {
            // The feed is cumulative — every poll returns the whole match history, so replaying
            // an event would double-count dragon stacks.
            if (gameEvent.EventId <= _lastEventId)
            {
                continue;
            }

            _lastEventId = gameEvent.EventId;
            Apply(gameEvent, snapshot);
        }
    }

    private void Apply(GameEvent gameEvent, GameSnapshot snapshot)
    {
        switch (gameEvent.EventName)
        {
            case "GameStart":
                Reset();
                _lastEventId = gameEvent.EventId;
                break;

            case "DragonKill":
                if (string.Equals(gameEvent.DragonType, "Elder", StringComparison.OrdinalIgnoreCase))
                {
                    _elderSpawnsAt = gameEvent.EventTime + _settings.ElderRespawn;
                    _elderBuffEndsAt = gameEvent.EventTime + _settings.ElderBuffDuration;
                    _elderBuffTeam = TeamOf(gameEvent.KillerName, snapshot) ?? string.Empty;
                }
                else
                {
                    _lastDragonType = gameEvent.DragonType ?? string.Empty;
                    _dragonSpawnsAt = gameEvent.EventTime + _settings.DragonRespawn;
                    Credit(_dragonsByTeam, gameEvent.KillerName, snapshot);
                }

                break;

            case "BaronKill":
                _baronSpawnsAt = gameEvent.EventTime + _settings.BaronRespawn;
                _baronBuffEndsAt = gameEvent.EventTime + _settings.BaronBuffDuration;
                _baronBuffTeam = TeamOf(gameEvent.KillerName, snapshot) ?? string.Empty;
                break;

            case "HeraldKill":
                _heraldSpawnsAt = gameEvent.EventTime + _settings.HeraldRespawn;
                break;

            // Verified against the game binary: "HordeKill" sits in the same string table as the
            // other event names and the payload field names, between DragonKill and HeraldKill.
            case "HordeKill":
                Credit(_voidgrubsByTeam, gameEvent.KillerName, snapshot);
                break;

            case "InhibKilled":
                if (!string.IsNullOrWhiteSpace(gameEvent.InhibKilled))
                {
                    _inhibitorsDown[gameEvent.InhibKilled] = gameEvent.EventTime + _settings.InhibitorRespawn;
                }

                break;

            // The id arrives in a field named after the event itself, so this cannot read
            // InhibKilled — doing so silently left every respawned inhibitor on the row until its
            // own timer expired.
            case "InhibRespawned":
                if (!string.IsNullOrWhiteSpace(gameEvent.InhibRespawned))
                {
                    _inhibitorsDown.Remove(gameEvent.InhibRespawned);
                }

                break;
        }
    }

    private void Credit(Dictionary<string, int> counter, string? killerName, GameSnapshot snapshot)
    {
        var team = TeamOf(killerName, snapshot);

        if (team is not null && counter.ContainsKey(team))
        {
            counter[team]++;
        }
    }

    /// <summary>
    /// Events name a player by their bare game name ("Ornek") while the player list carries the
    /// full Riot ID in every name field ("Ornek#0001"). Comparing those directly never matches, so
    /// both sides are reduced to the part before the tag.
    /// </summary>
    private static string? TeamOf(string? eventName, GameSnapshot snapshot)
    {
        if (string.IsNullOrWhiteSpace(eventName))
        {
            return null;
        }

        var bare = Bare(eventName);

        var player = snapshot.AllPlayers.FirstOrDefault(p =>
            string.Equals(Bare(p.RiotIdGameName), bare, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(Bare(p.RiotId), bare, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(Bare(p.SummonerName), bare, StringComparison.OrdinalIgnoreCase));

        return player?.Team;
    }

    private static string Bare(string? name) =>
        string.IsNullOrEmpty(name) ? string.Empty : name.Split('#')[0];

    public IReadOnlyList<ObjectiveState> GetObjectives(double gameTime)
    {
        var objectives = new List<ObjectiveState>();

        if (IsSummonersRift)
        {
            AddRiftObjectives(objectives, gameTime);
        }

        // Inhibitors exist on every map with lanes, so these stay regardless of mode.
        foreach (var (id, respawnsAt) in _inhibitorsDown.OrderBy(i => i.Value))
        {
            if (respawnsAt > gameTime)
            {
                objectives.Add(new ObjectiveState(ObjectiveKind.Inhibitor, "Inhib", Describe(id), respawnsAt));
            }
        }

        // Minion cadence differs per map, and the configured numbers describe the Rift only.
        if (IsSummonersRift)
        {
            var (nextWaveAt, cannonIn) = NextWave(gameTime);

            objectives.Add(new ObjectiveState(
                ObjectiveKind.Wave,
                "Dalga",
                cannonIn == 0 ? "top mermi bu dalgada" : $"top mermi {cannonIn} dalga sonra",
                nextWaveAt));
        }

        return objectives;
    }

    private void AddRiftObjectives(List<ObjectiveState> objectives, double gameTime)
    {
        if (gameTime < _settings.VoidgrubsDespawn)
        {
            objectives.Add(new ObjectiveState(
                ObjectiveKind.Voidgrubs,
                "Grub",
                $"{OrderVoidgrubs}-{ChaosVoidgrubs}",
                _settings.VoidgrubsFirstSpawn));
        }

        objectives.Add(new ObjectiveState(
            ObjectiveKind.Dragon,
            "Dragon",
            $"{OrderDragons}-{ChaosDragons}{(string.IsNullOrEmpty(_lastDragonType) ? string.Empty : $"  ·  {_lastDragonType}")}",
            _dragonSpawnsAt));

        if (_elderSpawnsAt is { } elder)
        {
            objectives.Add(new ObjectiveState(ObjectiveKind.Elder, "Elder", "yeniden doğuş", elder));
        }

        if (gameTime < _settings.HeraldDespawn)
        {
            objectives.Add(new ObjectiveState(ObjectiveKind.Herald, "Herald", "pit", _heraldSpawnsAt));
        }

        objectives.Add(new ObjectiveState(ObjectiveKind.Baron, "Baron", "pit", _baronSpawnsAt));

        // Buffs are transient: they drop out of the row when they expire rather than sitting
        // there reading "hazır", because a buff either is or is not active.
        if (_baronBuffEndsAt is { } baronBuff && baronBuff > gameTime)
        {
            objectives.Add(new ObjectiveState(
                ObjectiveKind.BaronBuff, "Baron buff", Side(_baronBuffTeam), baronBuff));
        }

        if (_elderBuffEndsAt is { } elderBuff && elderBuff > gameTime)
        {
            objectives.Add(new ObjectiveState(
                ObjectiveKind.ElderBuff, "Elder buff", Side(_elderBuffTeam), elderBuff));
        }
    }

    /// <returns>When the next minion wave spawns, and how many waves until one carries a cannon.</returns>
    public (double NextWaveAt, int WavesUntilCannon) NextWave(double gameTime)
    {
        var spawned = gameTime < _settings.FirstWave
            ? 0
            : (int)((gameTime - _settings.FirstWave) / _settings.WaveInterval) + 1;

        var next = spawned + 1;
        var nextAt = _settings.FirstWave + ((next - 1) * _settings.WaveInterval);

        var cannonWave = next;
        while (!HasCannon(cannonWave))
        {
            cannonWave++;
        }

        return (nextAt, cannonWave - next);
    }

    private bool HasCannon(int wave)
    {
        var spawnsAt = _settings.FirstWave + ((wave - 1) * _settings.WaveInterval);

        var cadence = spawnsAt < _settings.CannonCadenceChangesAt
            ? _settings.CannonEveryNthWave
            : _settings.CannonEveryNthWaveLate;

        return wave % cadence == 0;
    }

    private string Side(string team) => string.IsNullOrEmpty(team)
        ? string.Empty
        : string.Equals(team, AllyTeam, StringComparison.OrdinalIgnoreCase) ? "bizde" : "rakipte";

    /// <summary>
    /// Turns a structure id such as "Inhib_TChaos_L1_P1_1931666598_0" into a readable side and lane.
    /// </summary>
    private string Describe(string inhibitorId)
    {
        // Live structure ids name sides as TOrder/TChaos, but the older T1/T2 form still shows up
        // in places, so both are accepted.
        var team =
            inhibitorId.Contains("TOrder", StringComparison.OrdinalIgnoreCase) ||
            inhibitorId.Contains("_T1_", StringComparison.OrdinalIgnoreCase) ? "ORDER"
            : inhibitorId.Contains("TChaos", StringComparison.OrdinalIgnoreCase) ||
              inhibitorId.Contains("_T2_", StringComparison.OrdinalIgnoreCase) ? "CHAOS"
            : string.Empty;

        return string.Join(" · ", new[] { Side(team), Lane(inhibitorId) }.Where(s => s.Length > 0));
    }

    /// <summary>
    /// Lanes are numbered, not lettered, which rules out the obvious reading of L/C/R as left,
    /// centre and right. Turrets and inhibitors share the numbering, so one id parser serves both.
    /// <para>
    /// All three were observed live, and the numbering is zero-based from the bottom of the map:
    /// <c>L0</c> bottom, <c>L1</c> middle, <c>L2</c> top. There is no <c>L3</c> — assuming one, and
    /// that bottom would fill it, was wrong until a live bottom-lane push returned
    /// <c>Inhib_TChaos_L0_P1</c>. Anything outside 0–2 is left unnamed, because the side alone is
    /// useful and a missing word beats a confidently wrong one.
    /// </para>
    /// </summary>
    private static string Lane(string structureId)
    {
        var match = LanePattern().Match(structureId);

        return match.Success
            ? match.Groups[1].Value switch
            {
                "0" => "alt",
                "1" => "orta",
                "2" => "üst",
                _ => string.Empty,
            }
            : string.Empty;
    }

    [System.Text.RegularExpressions.GeneratedRegex(@"_L(\d+)", System.Text.RegularExpressions.RegexOptions.IgnoreCase)]
    private static partial System.Text.RegularExpressions.Regex LanePattern();
}
