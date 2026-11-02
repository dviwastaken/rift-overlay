using RiftOverlay.Models;
using RiftOverlay.Services;

namespace RiftOverlay.Tests;

public class ObjectiveTrackerTests
{
    private static readonly ObjectiveSettings Settings = new();

    [Fact]
    public void DragonKill_AnchorsRespawnToEventTime()
    {
        var tracker = new ObjectiveTracker(Settings);

        tracker.Process(Snapshot(Event(1, "DragonKill", 400, killer: "Ormanci", dragonType: "Infernal")));

        var dragon = Objective(tracker, ObjectiveKind.Dragon, gameTime: 400);

        Assert.Equal(400 + Settings.DragonRespawn, dragon.SpawnsAtGameTime);
    }

    [Fact]
    public void BaronKill_AnchorsRespawnToEventTime()
    {
        var tracker = new ObjectiveTracker(Settings);

        tracker.Process(Snapshot(Event(1, "BaronKill", 1300, killer: "Ormanci")));

        var baron = Objective(tracker, ObjectiveKind.Baron, gameTime: 1300);

        Assert.Equal(1300 + Settings.BaronRespawn, baron.SpawnsAtGameTime);
    }

    [Fact]
    public void BeforeAnyKill_ObjectivesUseConfiguredFirstSpawn()
    {
        var tracker = new ObjectiveTracker(Settings);

        tracker.Process(Snapshot(Event(0, "GameStart", 0)));

        Assert.Equal(Settings.DragonFirstSpawn, Objective(tracker, ObjectiveKind.Dragon, 60).SpawnsAtGameTime);
        Assert.Equal(Settings.BaronFirstSpawn, Objective(tracker, ObjectiveKind.Baron, 60).SpawnsAtGameTime);
        Assert.Equal(Settings.HeraldFirstSpawn, Objective(tracker, ObjectiveKind.Herald, 60).SpawnsAtGameTime);
    }

    /// <summary>
    /// The live feed is cumulative — every poll returns the whole match history. Replaying it must
    /// not credit the same dragon twice.
    /// </summary>
    [Fact]
    public void CumulativeFeed_ReplayedAcrossPolls_CountsEachDragonOnce()
    {
        var tracker = new ObjectiveTracker(Settings);
        var snapshot = Snapshot(
            Event(0, "GameStart", 0),
            Event(1, "DragonKill", 400, killer: "Ormanci", dragonType: "Infernal"),
            Event(2, "DragonKill", 760, killer: "Ormanci", dragonType: "Ocean"));

        tracker.Process(snapshot);
        tracker.Process(snapshot);
        tracker.Process(snapshot);

        Assert.Equal(2, tracker.OrderDragons);
        Assert.Equal(0, tracker.ChaosDragons);
    }

    [Fact]
    public void DragonKill_CreditsTheKillersTeam()
    {
        var tracker = new ObjectiveTracker(Settings);

        tracker.Process(Snapshot(
            Event(1, "DragonKill", 400, killer: "Ormanci", dragonType: "Infernal"),
            Event(2, "DragonKill", 760, killer: "Rakip2", dragonType: "Ocean")));

        Assert.Equal(1, tracker.OrderDragons);
        Assert.Equal(1, tracker.ChaosDragons);
    }

    [Fact]
    public void DragonKill_ByUnknownKiller_DoesNotCreditEitherTeam()
    {
        var tracker = new ObjectiveTracker(Settings);

        tracker.Process(Snapshot(Event(1, "DragonKill", 400, killer: "SFX_Turret", dragonType: "Infernal")));

        Assert.Equal(0, tracker.OrderDragons);
        Assert.Equal(0, tracker.ChaosDragons);
    }

    [Fact]
    public void GameStart_ResetsPreviousMatchState()
    {
        var tracker = new ObjectiveTracker(Settings);

        tracker.Process(Snapshot(Event(1, "DragonKill", 400, killer: "Ormanci", dragonType: "Infernal")));
        tracker.Process(Snapshot(Event(2, "GameStart", 0)));

        Assert.Equal(0, tracker.OrderDragons);
        Assert.Equal(Settings.DragonFirstSpawn, Objective(tracker, ObjectiveKind.Dragon, 60).SpawnsAtGameTime);
    }

    [Fact]
    public void Herald_IsHiddenOnceBaronTakesOverThePit()
    {
        var tracker = new ObjectiveTracker(Settings);

        Assert.Contains(tracker.GetObjectives(Settings.HeraldDespawn - 1), o => o.Kind == ObjectiveKind.Herald);
        Assert.DoesNotContain(tracker.GetObjectives(Settings.HeraldDespawn + 1), o => o.Kind == ObjectiveKind.Herald);
    }

    [Fact]
    public void HeraldKill_SchedulesTheSecondHerald()
    {
        var tracker = new ObjectiveTracker(Settings);

        tracker.Process(Snapshot(Event(1, "HeraldKill", 850, killer: "Ormanci")));

        var herald = Objective(tracker, ObjectiveKind.Herald, gameTime: 850);

        Assert.Equal(850 + Settings.HeraldRespawn, herald.SpawnsAtGameTime);
    }

    [Fact]
    public void Elder_IsAbsentUntilAnElderIsKilled()
    {
        var tracker = new ObjectiveTracker(Settings);

        Assert.DoesNotContain(tracker.GetObjectives(1500), o => o.Kind == ObjectiveKind.Elder);

        tracker.Process(Snapshot(Event(1, "DragonKill", 1500, killer: "Ormanci", dragonType: "Elder")));

        var elder = Objective(tracker, ObjectiveKind.Elder, gameTime: 1500);
        Assert.Equal(1500 + Settings.ElderRespawn, elder.SpawnsAtGameTime);
    }

    /// <summary>An elder kill is still a DragonKill event, but must not count toward dragon soul.</summary>
    [Fact]
    public void ElderKill_DoesNotCountAsADragonStack()
    {
        var tracker = new ObjectiveTracker(Settings);

        tracker.Process(Snapshot(Event(1, "DragonKill", 1500, killer: "Ormanci", dragonType: "Elder")));

        Assert.Equal(0, tracker.OrderDragons);
        Assert.Equal(Settings.DragonFirstSpawn, Objective(tracker, ObjectiveKind.Dragon, 1500).SpawnsAtGameTime);
    }

    private static ObjectiveState Objective(ObjectiveTracker tracker, ObjectiveKind kind, double gameTime) =>
        tracker.GetObjectives(gameTime).Single(o => o.Kind == kind);

    private static GameEvent Event(
        int id, string name, double time, string? killer = null, string? dragonType = null) => new()
        {
            EventId = id,
            EventName = name,
            EventTime = time,
            KillerName = killer,
            DragonType = dragonType,
        };

    private static GameSnapshot Snapshot(params GameEvent[] events) => new()
    {
        ActivePlayer = new ActivePlayer { SummonerName = "MidLaner", RiotIdGameName = "MidLaner" },
        AllPlayers =
        [
            Player("Ormanci", "Viego", "ORDER"),
            Player("MidLaner", "Orianna", "ORDER"),
            Player("Rakip2", "Lee Sin", "CHAOS"),
            Player("Rakip3", "Ahri", "CHAOS"),
        ],
        Events = new EventContainer { Events = [.. events] },
    };

    /// <summary>
    /// Mirrors the live payload: every name field carries the full Riot ID, while event fields
    /// elsewhere use the bare game name.
    /// </summary>
    private static PlayerState Player(string name, string champion, string team) => new()
    {
        SummonerName = $"{name}#TR1",
        RiotId = $"{name}#TR1",
        RiotIdGameName = name,
        ChampionName = champion,
        Team = team,
    };
}
