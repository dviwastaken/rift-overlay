using RiftOverlay.Models;
using RiftOverlay.Services;

namespace RiftOverlay.Tests;

/// <summary>Covers the transient counters: buffs, downed inhibitors, void grubs and minion waves.</summary>
public class ObjectiveTrackerCountersTests
{
    private static readonly ObjectiveSettings Settings = new();

    [Fact]
    public void BaronKill_StartsABuffTimerForTheKillingTeam()
    {
        var tracker = Tracker();

        tracker.Process(Snapshot(Event(1, "BaronKill", 1300, killer: "Ormanci")));

        var buff = Single(tracker, ObjectiveKind.BaronBuff, gameTime: 1300);

        Assert.Equal(1300 + Settings.BaronBuffDuration, buff.SpawnsAtGameTime);
        Assert.Equal("bizde", buff.Detail);
    }

    [Fact]
    public void EnemyBaron_IsLabelledAsTheirs()
    {
        var tracker = Tracker();

        tracker.Process(Snapshot(Event(1, "BaronKill", 1300, killer: "Rakip2")));

        Assert.Equal("rakipte", Single(tracker, ObjectiveKind.BaronBuff, 1300).Detail);
    }

    /// <summary>A buff is transient — it leaves the row rather than sitting there reading "hazır".</summary>
    [Fact]
    public void BaronBuff_DisappearsWhenItExpires()
    {
        var tracker = Tracker();

        tracker.Process(Snapshot(Event(1, "BaronKill", 1300, killer: "Ormanci")));

        var justBefore = 1300 + Settings.BaronBuffDuration - 1;
        Assert.Contains(tracker.GetObjectives(justBefore), o => o.Kind == ObjectiveKind.BaronBuff);
        Assert.DoesNotContain(
            tracker.GetObjectives(justBefore + 2), o => o.Kind == ObjectiveKind.BaronBuff);
    }

    [Fact]
    public void ElderKill_StartsBothTheRespawnAndTheBuffTimer()
    {
        var tracker = Tracker();

        tracker.Process(Snapshot(Event(1, "DragonKill", 1500, killer: "Ormanci", dragonType: "Elder")));

        Assert.Equal(1500 + Settings.ElderRespawn, Single(tracker, ObjectiveKind.Elder, 1500).SpawnsAtGameTime);
        Assert.Equal(1500 + Settings.ElderBuffDuration, Single(tracker, ObjectiveKind.ElderBuff, 1500).SpawnsAtGameTime);
    }

    [Fact]
    public void InhibKilled_AddsARespawnCounterNamingSideAndLane()
    {
        var tracker = Tracker();

        tracker.Process(Snapshot(Event(1, "InhibKilled", 1400, inhibitor: "Inhib_TChaos_L1_P1_1931666598_0")));

        var inhib = Single(tracker, ObjectiveKind.Inhibitor, gameTime: 1400);

        Assert.Equal(1400 + Settings.InhibitorRespawn, inhib.SpawnsAtGameTime);
        Assert.Equal("rakipte · orta", inhib.Detail);
    }

    [Fact]
    public void EachDownedInhibitor_GetsItsOwnCounter()
    {
        var tracker = Tracker();

        tracker.Process(Snapshot(
            Event(1, "InhibKilled", 1400, inhibitor: "Inhib_TChaos_L1_P1_1931666598_0"),
            Event(2, "InhibKilled", 1430, inhibitor: "Inhib_TChaos_L0_P1_2116220407_0")));

        Assert.Equal(2, tracker.GetObjectives(1440).Count(o => o.Kind == ObjectiveKind.Inhibitor));
    }

    [Fact]
    public void InhibRespawned_RemovesTheCounterEvenBeforeItsTimerWouldElapse()
    {
        var tracker = Tracker();

        tracker.Process(Snapshot(
            Event(1, "InhibKilled", 1400, inhibitor: "Inhib_TChaos_L1_P1_1931666598_0"),
            Event(2, "InhibRespawned", 1500, respawned: "Inhib_TChaos_L1_P1_1931666598_0")));

        Assert.DoesNotContain(tracker.GetObjectives(1510), o => o.Kind == ObjectiveKind.Inhibitor);
    }

    [Fact]
    public void DownedInhibitor_DropsOutOnceItRespawns()
    {
        var tracker = Tracker();

        tracker.Process(Snapshot(Event(1, "InhibKilled", 1400, inhibitor: "Inhib_TChaos_L1_P1_1931666598_0")));

        Assert.Contains(tracker.GetObjectives(1600), o => o.Kind == ObjectiveKind.Inhibitor);
        Assert.DoesNotContain(
            tracker.GetObjectives(1400 + Settings.InhibitorRespawn + 1), o => o.Kind == ObjectiveKind.Inhibitor);
    }

    [Fact]
    public void VoidgrubKills_AreCountedPerTeam()
    {
        var tracker = Tracker();

        tracker.Process(Snapshot(
            Event(1, "HordeKill", 400, killer: "Ormanci"),
            Event(2, "HordeKill", 405, killer: "Ormanci"),
            Event(3, "HordeKill", 500, killer: "Rakip2")));

        Assert.Equal(2, tracker.OrderVoidgrubs);
        Assert.Equal(1, tracker.ChaosVoidgrubs);
        Assert.Equal("2-1", Single(tracker, ObjectiveKind.Voidgrubs, 520).Detail);
    }

    [Fact]
    public void Voidgrubs_LeaveTheRowOnceTheyStopSpawning()
    {
        var tracker = Tracker();

        Assert.Contains(tracker.GetObjectives(Settings.VoidgrubsDespawn - 1), o => o.Kind == ObjectiveKind.Voidgrubs);
        Assert.DoesNotContain(
            tracker.GetObjectives(Settings.VoidgrubsDespawn + 1), o => o.Kind == ObjectiveKind.Voidgrubs);
    }

    [Theory]
    // Before the first wave the countdown points at it, and the first cannon is two waves later.
    [InlineData(0, 65, 2)]
    [InlineData(70, 95, 1)]
    [InlineData(130, 155, 2)]
    public void WaveCountdown_TracksTheThirtySecondCadence(
        double gameTime, double expectedNextWave, int expectedCannonIn)
    {
        var (nextWaveAt, cannonIn) = Tracker().NextWave(gameTime);

        Assert.Equal(expectedNextWave, nextWaveAt, precision: 3);
        Assert.Equal(expectedCannonIn, cannonIn);
    }

    /// <summary>Cannons arrive every other wave once the late-game cadence kicks in.</summary>
    [Fact]
    public void WaveCountdown_SwitchesToTheLateCannonCadence()
    {
        var (nextWaveAt, cannonIn) = Tracker().NextWave(1000);

        Assert.Equal(1025, nextWaveAt, precision: 3);
        Assert.Equal(1, cannonIn);
    }

    [Fact]
    public void GameStart_ClearsBuffsAndDownedInhibitors()
    {
        var tracker = Tracker();

        tracker.Process(Snapshot(
            Event(1, "BaronKill", 1300, killer: "Ormanci"),
            Event(2, "InhibKilled", 1400, inhibitor: "Inhib_TChaos_L1_P1_1931666598_0"),
            Event(3, "HordeKill", 400, killer: "Ormanci")));

        tracker.Process(Snapshot(Event(4, "GameStart", 0)));

        Assert.DoesNotContain(tracker.GetObjectives(1420), o => o.Kind == ObjectiveKind.BaronBuff);
        Assert.DoesNotContain(tracker.GetObjectives(1420), o => o.Kind == ObjectiveKind.Inhibitor);
        Assert.Equal(0, tracker.OrderVoidgrubs);
    }

    private static ObjectiveTracker Tracker() => new(Settings) { AllyTeam = "ORDER" };

    private static ObjectiveState Single(ObjectiveTracker tracker, ObjectiveKind kind, double gameTime) =>
        tracker.GetObjectives(gameTime).Single(o => o.Kind == kind);

    private static GameEvent Event(
        int id,
        string name,
        double time,
        string? killer = null,
        string? dragonType = null,
        string? inhibitor = null,
        string? respawned = null) => new()
        {
            EventId = id,
            EventName = name,
            EventTime = time,
            KillerName = killer,
            DragonType = dragonType,
            InhibKilled = inhibitor,
            InhibRespawned = respawned,
        };

    private static GameSnapshot Snapshot(params GameEvent[] events) => new()
    {
        ActivePlayer = new ActivePlayer { SummonerName = "MidLaner", RiotIdGameName = "MidLaner" },
        AllPlayers =
        [
            Player("Ormanci", "Viego", "ORDER"),
            Player("MidLaner", "Orianna", "ORDER"),
            Player("Rakip2", "Lee Sin", "CHAOS"),
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
