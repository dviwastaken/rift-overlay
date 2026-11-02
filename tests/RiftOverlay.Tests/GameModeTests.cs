using RiftOverlay.Models;
using RiftOverlay.Services;

namespace RiftOverlay.Tests;

/// <summary>
/// Objective counters describe Summoner's Rift. Outside it most of them are inventing things that
/// are not in the game, so the row has to shrink to what the mode actually has.
/// </summary>
public class GameModeTests
{
    private static readonly ObjectiveKind[] RiftOnly =
    [
        ObjectiveKind.Dragon,
        ObjectiveKind.Elder,
        ObjectiveKind.Baron,
        ObjectiveKind.Herald,
        ObjectiveKind.BaronBuff,
        ObjectiveKind.ElderBuff,
        ObjectiveKind.Voidgrubs,
        ObjectiveKind.Wave,
    ];

    [Fact]
    public void OnTheRift_TheFullSetIsShown()
    {
        var tracker = Tracker("Map11");

        var kinds = tracker.GetObjectives(600).Select(o => o.Kind).ToList();

        Assert.Contains(ObjectiveKind.Dragon, kinds);
        Assert.Contains(ObjectiveKind.Baron, kinds);
        Assert.Contains(ObjectiveKind.Herald, kinds);
        Assert.Contains(ObjectiveKind.Voidgrubs, kinds);
        Assert.Contains(ObjectiveKind.Wave, kinds);
    }

    [Theory]
    [InlineData("Map12")] // ARAM
    [InlineData("Map21")] // Nexus Blitz
    [InlineData("Map30")] // Arena
    public void OffTheRift_NoRiftObjectiveIsShown(string mapName)
    {
        var tracker = Tracker(mapName);

        // Feed it the events anyway — the filter must be about the map, not about what happened.
        tracker.Process(Snapshot(
            Event(1, "DragonKill", 400, killer: "Ormanci", dragonType: "Infernal"),
            Event(2, "BaronKill", 1300, killer: "Ormanci")));

        var kinds = tracker.GetObjectives(1320).Select(o => o.Kind).ToList();

        Assert.DoesNotContain(kinds, RiftOnly.Contains);
    }

    [Fact]
    public void OffTheRift_InhibitorsAreStillTracked()
    {
        var tracker = Tracker("Map12");

        tracker.Process(Snapshot(Event(1, "InhibKilled", 1400, inhibitor: "Barracks_T2_C1")));

        Assert.Contains(tracker.GetObjectives(1420), o => o.Kind == ObjectiveKind.Inhibitor);
    }

    /// <summary>
    /// A missing map name means the field did not arrive, not that we are somewhere else — the
    /// Rift set stays.
    /// </summary>
    [Fact]
    public void AMissingMapName_FallsBackToTheRiftSet()
    {
        Assert.Contains(Tracker("").GetObjectives(600), o => o.Kind == ObjectiveKind.Dragon);
    }

    /// <summary>
    /// An unrecognised map id is far more likely to be a mode Riot added than a new Rift, so it
    /// gets the conservative treatment rather than a Baron timer for a pit that may not exist.
    /// </summary>
    [Fact]
    public void AnUnrecognisedMap_IsTreatedAsNotTheRift()
    {
        Assert.DoesNotContain(Tracker("Map99").GetObjectives(600), o => o.Kind == ObjectiveKind.Dragon);
    }

    private static ObjectiveTracker Tracker(string mapName) =>
        new() { AllyTeam = "ORDER", MapName = mapName };

    private static GameEvent Event(
        int id,
        string name,
        double time,
        string? killer = null,
        string? dragonType = null,
        string? inhibitor = null) => new()
        {
            EventId = id,
            EventName = name,
            EventTime = time,
            KillerName = killer,
            DragonType = dragonType,
            InhibKilled = inhibitor,
        };

    private static GameSnapshot Snapshot(params GameEvent[] events) => new()
    {
        AllPlayers =
        [
            new PlayerState { SummonerName = "Ormanci", RiotId = "Ormanci#TR1", Team = "ORDER" },
        ],
        Events = new EventContainer { Events = [.. events] },
    };
}
