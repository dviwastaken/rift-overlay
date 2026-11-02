using System.Diagnostics;
using RiftOverlay.Models;

namespace RiftOverlay.Services;

/// <summary>
/// Replays a scripted match so the overlay can be developed and laid out without queueing up.
/// Emits exactly the same <see cref="GameSnapshot"/> shape as <see cref="LiveClientService"/>.
/// </summary>
public sealed class DemoDataSource : IGameDataSource
{
    /// <summary>Objective timings squeezed down so a countdown actually resolves while watching.</summary>
    public static ObjectiveSettings CompressedSettings => new()
    {
        DragonFirstSpawn = 25,
        DragonRespawn = 45,
        ElderRespawn = 50,
        BaronFirstSpawn = 70,
        BaronRespawn = 55,
        HeraldFirstSpawn = 35,
        HeraldRespawn = 40,
        HeraldDespawn = 10 * 60,
        BaronBuffDuration = 40,
        ElderBuffDuration = 35,
        InhibitorRespawn = 60,
        VoidgrubsFirstSpawn = 15,
        VoidgrubsDespawn = 8 * 60,
        FirstWave = 12,
        WaveInterval = 15,
        CannonCadenceChangesAt = 4 * 60,
    };

    private readonly record struct ScriptedEvent(
        double Time,
        string Name,
        string? Killer = null,
        string? Victim = null,
        string? DragonType = null,
        string? Inhibitor = null,
        double RespawnSeconds = 0);

    private static readonly (string Champion, string Player, string Position, string Team)[] Roster =
    [
        ("Aatrox", "Oyuncu", "TOP", "ORDER"),
        ("Viego", "Ormanci", "JUNGLE", "ORDER"),
        ("Orianna", "MidLaner", "MIDDLE", "ORDER"),
        ("Jinx", "AdcAbi", "BOTTOM", "ORDER"),
        ("Thresh", "Destek", "UTILITY", "ORDER"),
        ("Renekton", "Rakip1", "TOP", "CHAOS"),
        ("Lee Sin", "Rakip2", "JUNGLE", "CHAOS"),
        ("Ahri", "Rakip3", "MIDDLE", "CHAOS"),
        ("Caitlyn", "Rakip4", "BOTTOM", "CHAOS"),
        ("Lulu", "Rakip5", "UTILITY", "CHAOS"),
    ];

    private readonly List<ScriptedEvent> _script = [];
    private readonly List<GameEvent> _emitted = [];
    private readonly Dictionary<string, (double DiesAt, double RespawnSeconds)> _deaths = new();
    private readonly Dictionary<string, PlayerScores> _scores = new();
    private readonly Stopwatch _clock = new();
    private readonly CancellationTokenSource _cts = new();
    private readonly SynchronizationContext? _context = SynchronizationContext.Current;

    private int _nextScriptIndex;
    private int _nextEventId;

    public DemoDataSource()
    {
        foreach (var (_, player, _, _) in Roster)
        {
            _scores[player] = new PlayerScores();
        }

        BuildScript();
    }

    public event EventHandler<GameSnapshot>? SnapshotReceived;

    public event EventHandler<DataSourceState>? StateChanged;

    public void Start()
    {
        _clock.Start();
        Post(() => StateChanged?.Invoke(this, DataSourceState.Connected));
        _ = Task.Run(() => LoopAsync(_cts.Token));
    }

    private void BuildScript()
    {
        var random = new Random(1907);

        _script.Add(new ScriptedEvent(0, "GameStart"));

        _script.Add(new ScriptedEvent(8, "ChampionKill", "AdcAbi", "Rakip4", RespawnSeconds: 14));
        _script.Add(new ScriptedEvent(18, "HordeKill", "Ormanci"));
        _script.Add(new ScriptedEvent(20, "HordeKill", "Ormanci"));
        _script.Add(new ScriptedEvent(23, "HordeKill", "Rakip2"));
        _script.Add(new ScriptedEvent(30, "TurretKilled"));
        _script.Add(new ScriptedEvent(36, "DragonKill", "Ormanci", DragonType: "Infernal"));
        _script.Add(new ScriptedEvent(48, "ChampionKill", "Rakip2", "Oyuncu", RespawnSeconds: 19));
        _script.Add(new ScriptedEvent(55, "HeraldKill", "Ormanci"));
        _script.Add(new ScriptedEvent(70, "ChampionKill", "MidLaner", "Rakip3", RespawnSeconds: 22));
        _script.Add(new ScriptedEvent(88, "BaronKill", "Ormanci"));
        _script.Add(new ScriptedEvent(100, "InhibKilled", Inhibitor: "Barracks_T2_C1"));
        _script.Add(new ScriptedEvent(112, "DragonKill", "Rakip2", DragonType: "Ocean"));
        _script.Add(new ScriptedEvent(140, "DragonKill", "Ormanci", DragonType: "Elder"));

        // Keep the demo alive indefinitely with a trickle of kills after the scripted opening.
        for (var time = 110d; time < 3600; time += 22)
        {
            var attacker = Roster[random.Next(Roster.Length)];
            var victim = Roster.Where(r => r.Team != attacker.Team).ElementAt(random.Next(5));

            _script.Add(new ScriptedEvent(
                time, "ChampionKill", attacker.Player, victim.Player, RespawnSeconds: 12 + random.Next(30)));
        }
    }

    private async Task LoopAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            var snapshot = BuildSnapshot();
            Post(() => SnapshotReceived?.Invoke(this, snapshot));

            try
            {
                await Task.Delay(TimeSpan.FromMilliseconds(250), token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    private GameSnapshot BuildSnapshot()
    {
        var gameTime = _clock.Elapsed.TotalSeconds;

        while (_nextScriptIndex < _script.Count && _script[_nextScriptIndex].Time <= gameTime)
        {
            Emit(_script[_nextScriptIndex]);
            _nextScriptIndex++;
        }

        var players = new List<PlayerState>(Roster.Length);

        foreach (var (champion, player, position, team) in Roster)
        {
            var remaining = 0d;

            if (_deaths.TryGetValue(player, out var death))
            {
                remaining = Math.Max(0, death.DiesAt + death.RespawnSeconds - gameTime);
            }

            players.Add(new PlayerState
            {
                ChampionName = champion,
                SummonerName = player,
                RiotId = $"{player}#TR1",
                Position = position,
                Team = team,
                Level = Math.Clamp(1 + (int)(gameTime / 45), 1, 18),
                IsDead = remaining > 0,
                RespawnTimer = remaining,
                Scores = _scores[player],
                SummonerSpells = SpellsFor(player, team),
                Items = ItemsFor(player),
                Runes = RunesFor(player),
            });
        }

        return new GameSnapshot
        {
            ActivePlayer = new ActivePlayer
            {
                SummonerName = "MidLaner",
                RiotIdGameName = "MidLaner",
                Level = 9,
                CurrentGold = 180 + (gameTime * 7 % 1500),
            },
            AllPlayers = players,
            Events = new EventContainer { Events = [.. _emitted] },
            GameData = new GameStats { GameMode = "CLASSIC", GameTime = gameTime, MapName = "Map11" },
        };
    }

    // Deliberately uneven so the demo exercises every branch of the cooldown maths: Flash in either
    // slot, Lucidity boots, and an Inspiration tree that makes a timer uncertain.
    private static SummonerSpells SpellsFor(string player, string team) => player switch
    {
        "Rakip2" => Spells("Smite", "Flash"),
        "Rakip5" => Spells("Exhaust", "Flash"),
        _ => Spells("Flash", team == "ORDER" ? "Ignite" : "Teleport"),
    };

    private static SummonerSpells Spells(string first, string second) => new()
    {
        SummonerSpellOne = new SummonerSpell { DisplayName = first },
        SummonerSpellTwo = new SummonerSpell { DisplayName = second },
    };

    private static List<PlayerItem> ItemsFor(string player) => player switch
    {
        "Rakip1" => [new PlayerItem { ItemId = 3158, DisplayName = "Ionian Boots of Lucidity", Slot = 0 }],

        // Sheen plus a Caulfield's: enough of a Trinity Force to give the build panel something
        // to infer, and ambiguous enough to exercise the "more than one candidate" path.
        "MidLaner" =>
        [
            new PlayerItem { ItemId = 3057, DisplayName = "Sheen", Slot = 0 },
            new PlayerItem { ItemId = 3133, DisplayName = "Caulfield's Warhammer", Slot = 1 },
        ],

        _ => [],
    };

    private static PlayerRunes RunesFor(string player) => player switch
    {
        "Rakip1" => Runes(8000, 8300),
        "Rakip3" => Runes(8300, 8400),
        _ => Runes(8000, 8400),
    };

    private static PlayerRunes Runes(int primary, int secondary) => new()
    {
        PrimaryRuneTree = new RuneInfo { Id = primary },
        SecondaryRuneTree = new RuneInfo { Id = secondary },
    };

    private void Emit(ScriptedEvent scripted)
    {
        _emitted.Add(new GameEvent
        {
            EventId = _nextEventId++,
            EventName = scripted.Name,
            EventTime = scripted.Time,
            KillerName = scripted.Killer,
            VictimName = scripted.Victim,
            DragonType = scripted.DragonType,
            InhibKilled = scripted.Inhibitor,
        });

        if (scripted.Name != "ChampionKill" || scripted.Victim is null || scripted.Killer is null)
        {
            return;
        }

        _deaths[scripted.Victim] = (scripted.Time, scripted.RespawnSeconds);
        _scores[scripted.Victim].Deaths++;
        _scores[scripted.Killer].Kills++;
    }

    private void Post(Action action)
    {
        if (_context is null)
        {
            action();
        }
        else
        {
            _context.Post(_ => action(), null);
        }
    }

    public void Dispose()
    {
        _cts.Cancel();
        _cts.Dispose();
    }
}
