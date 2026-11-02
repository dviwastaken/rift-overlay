using System.IO;
using RiftOverlay.Models;
using RiftOverlay.Services;

namespace RiftOverlay.Tests;

/// <summary>
/// Covers recording a match end to end. Every test drives a real SQLite file in the temp directory,
/// because the interesting failures here — a cumulative feed written twice, a unique index that does
/// or does not hold — live in the storage layer and vanish under a mocked one.
/// </summary>
public sealed class MatchRecordingTests : IDisposable
{
    private readonly string _path = Path.Combine(
        Path.GetTempPath(), $"riftoverlay-test-{Guid.NewGuid():N}.db");

    private MatchDatabase Database() => new(_path);

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();

        if (File.Exists(_path))
        {
            File.Delete(_path);
        }
    }

    [Fact]
    public void TheFirstSnapshot_OpensAMatch()
    {
        var database = Database();
        var recorder = new MatchRecorder(database);

        recorder.OnSnapshot(Snapshot(gameTime: 30));

        var match = Assert.Single(database.GetRecentMatches());
        Assert.Equal("Orianna", match.Champion);
        Assert.Equal("CLASSIC", match.GameMode);
        Assert.Equal("ORDER", match.Team);
        Assert.Null(match.EndedAtUtc);
    }

    [Fact]
    public void ASnapshotWithoutTheActivePlayer_RecordsNothing()
    {
        var database = Database();
        var recorder = new MatchRecorder(database);

        // Happens on the first polls of a game, before the scoreboard is populated.
        recorder.OnSnapshot(new GameSnapshot());

        Assert.Empty(database.GetRecentMatches());
        Assert.Null(recorder.CurrentMatchId);
    }

    [Fact]
    public void TheCumulativeEventFeed_IsWrittenOnlyOnce()
    {
        var database = Database();
        var recorder = new MatchRecorder(database);

        // The live feed repeats every earlier event on every poll. Four polls of a growing feed
        // must leave three rows, not ten — this is the failure the whole id guard exists for.
        recorder.OnSnapshot(Snapshot(30, Kill(1, 25)));
        recorder.OnSnapshot(Snapshot(45, Kill(1, 25), Kill(2, 40)));
        recorder.OnSnapshot(Snapshot(60, Kill(1, 25), Kill(2, 40)));
        recorder.OnSnapshot(Snapshot(75, Kill(1, 25), Kill(2, 40), Kill(3, 70)));

        var events = database.GetEvents(recorder.CurrentMatchId!.Value);
        Assert.Equal([1, 2, 3], events.Select(e => e.EventId));
    }

    [Fact]
    public void ReplayingTheWholeFeed_CannotDoubleCount()
    {
        // Belt and braces: even if the recorder's own guard were bypassed, the unique index on
        // (MatchId, EventId) plus INSERT OR IGNORE has to make a repeated write a no-op.
        var database = Database();
        var matchId = database.StartMatch("CLASSIC", "Map11", "Orianna", "MIDDLE", "ORDER");

        var rows = new List<MatchEventRow> { new(1, "ChampionKill", 25, "a → b") };
        database.SaveEvents(matchId, rows);
        database.SaveEvents(matchId, rows);

        Assert.Single(database.GetEvents(matchId));
    }

    [Fact]
    public void AClockThatGoesBackwards_StartsASecondMatch()
    {
        var database = Database();
        var recorder = new MatchRecorder(database);

        recorder.OnSnapshot(Snapshot(gameTime: 900));
        recorder.OnSnapshot(Snapshot(gameTime: 20));

        Assert.Equal(2, database.GetRecentMatches().Count);
    }

    [Fact]
    public void ASmallClockWobble_DoesNotStartASecondMatch()
    {
        var database = Database();
        var recorder = new MatchRecorder(database);

        // The clock is a double sampled over a network hop; a hair of jitter is not a new game.
        recorder.OnSnapshot(Snapshot(gameTime: 900));
        recorder.OnSnapshot(Snapshot(gameTime: 899.8));

        Assert.Single(database.GetRecentMatches());
    }

    [Fact]
    public void GameEnd_ClosesTheMatchWithItsResult()
    {
        var database = Database();
        var recorder = new MatchRecorder(database);

        recorder.OnSnapshot(Snapshot(1500));
        recorder.OnSnapshot(Snapshot(1520, End(result: "Win")));

        var match = Assert.Single(database.GetRecentMatches());
        Assert.Equal("Win", match.Result);
        Assert.NotNull(match.EndedAtUtc);
        Assert.Null(recorder.CurrentMatchId);
    }

    [Fact]
    public void GameEnd_IsAlsoReadFromAFieldNamedAfterTheEvent()
    {
        // The three values are confirmed from the game binary but the field name is not, and every
        // structure event names its payload field after itself. Both spellings have to work.
        var database = Database();
        var recorder = new MatchRecorder(database);

        recorder.OnSnapshot(Snapshot(1500));
        recorder.OnSnapshot(Snapshot(1520, End(gameEndField: "Lose")));

        Assert.Equal("Lose", Assert.Single(database.GetRecentMatches()).Result);
    }

    [Fact]
    public void TheFinalScoreboard_IsRecordedBeforeTheMatchCloses()
    {
        var database = Database();
        var recorder = new MatchRecorder(database);

        recorder.OnSnapshot(Snapshot(1500, kills: 4));

        // The snapshot carrying GameEnd also carries the last kill; closing first would drop it.
        recorder.OnSnapshot(Snapshot(1520, kills: 5, events: End(result: "Win")));

        var match = Assert.Single(database.GetRecentMatches());
        Assert.Equal(5, match.Kills);
        Assert.Equal("Win", match.Result);
    }

    [Fact]
    public void LosingTheGameWithoutAGameEnd_ClosesTheMatchWithNoResult()
    {
        var database = Database();
        var recorder = new MatchRecorder(database);

        recorder.OnSnapshot(Snapshot(600));
        recorder.OnStateChanged(DataSourceState.Waiting);

        var match = Assert.Single(database.GetRecentMatches());
        Assert.NotNull(match.EndedAtUtc);
        Assert.Null(match.Result);
    }

    [Fact]
    public void AMatchLeftOpenByACrash_IsClosedByTheNextRun()
    {
        var database = Database();
        database.StartMatch("CLASSIC", "Map11", "Orianna", "MIDDLE", "ORDER");

        _ = new MatchRecorder(Database());

        var match = Assert.Single(database.GetRecentMatches());
        Assert.NotNull(match.EndedAtUtc);
        Assert.Null(match.Result);
    }

    [Fact]
    public void SamplesAreTakenOnTheGameClock_NotPerSnapshot()
    {
        var database = Database();
        var recorder = new MatchRecorder(database);

        // Snapshots arrive four times a second; sampling all of them would be thousands of rows.
        foreach (var tick in Enumerable.Range(0, 200))
        {
            recorder.OnSnapshot(Snapshot(tick * 0.25));
        }

        var samples = database.GetSamples(recorder.CurrentMatchId!.Value);

        Assert.Equal(4, samples.Count);
        Assert.Equal([0, 15, 30, 45], samples.Select(s => s.GameTime));
    }

    [Fact]
    public void WinRate_CountsOnlyMatchesWhoseResultWasSeen()
    {
        var database = Database();

        Finish(database, "Orianna", "Win");
        Finish(database, "Orianna", "Lose");
        Finish(database, "Orianna", null);

        // A match closed by a crash counts towards experience but must not count as a loss.
        var stat = Assert.Single(database.GetChampionStats());
        Assert.Equal(3, stat.Matches);
        Assert.Equal(2, stat.DecidedMatches);
        Assert.Equal(1, stat.Wins);
    }

    [Fact]
    public void CreepScoreAtTen_UsesTheLastSampleBeforeTheMark()
    {
        var database = Database();
        var matchId = database.StartMatch("CLASSIC", "Map11", "Orianna", "MIDDLE", "ORDER");

        database.SaveSample(matchId, Sample(gameTime: 570, creepScore: 61));
        database.SaveSample(matchId, Sample(gameTime: 599, creepScore: 68));

        // Past the mark: proves the match reached ten minutes, but must not be the figure taken.
        database.SaveSample(matchId, Sample(gameTime: 615, creepScore: 72));
        database.EndMatch(matchId, "Win");

        Assert.Equal(68, Assert.Single(database.GetChampionStats()).AverageCreepScoreAtTen);
    }

    [Fact]
    public void AMatchTooShortToReachTenMinutes_ContributesNoCreepScoreAtTen()
    {
        var database = Database();
        var matchId = database.StartMatch("CLASSIC", "Map11", "Orianna", "MIDDLE", "ORDER");

        // A remake at eight minutes has a CS figure, but it is not a ten-minute one and must not
        // be averaged in as though it were.
        database.SaveSample(matchId, Sample(gameTime: 480, creepScore: 55));
        database.EndMatch(matchId, "Neither");

        Assert.Null(Assert.Single(database.GetChampionStats()).AverageCreepScoreAtTen);
    }

    [Fact]
    public void StatsCoverFinishedMatchesOnly()
    {
        var database = Database();

        Finish(database, "Orianna", "Win");
        database.StartMatch("CLASSIC", "Map11", "Ahri", "MIDDLE", "ORDER");

        Assert.Equal(["Orianna"], database.GetChampionStats().Select(s => s.Champion));
    }

    private static void Finish(MatchDatabase database, string champion, string? result)
    {
        var matchId = database.StartMatch("CLASSIC", "Map11", champion, "MIDDLE", "ORDER");
        database.EndMatch(matchId, result);
    }

    private static MatchSample Sample(double gameTime, int creepScore) =>
        new(gameTime, 0, 0, 0, creepScore, 0, 1, 500);

    private static GameEvent Kill(int eventId, double eventTime) => new()
    {
        EventId = eventId,
        EventName = "ChampionKill",
        EventTime = eventTime,
        KillerName = "Oyuncu",
        VictimName = "Sion Bot",
    };

    private static GameEvent[] End(string? result = null, string? gameEndField = null) =>
    [
        new GameEvent
        {
            EventId = 99,
            EventName = "GameEnd",
            EventTime = 1519,
            Result = result,
            GameEndResult = gameEndField,
        },
    ];

    private static GameSnapshot Snapshot(double gameTime, params GameEvent[] events) =>
        Snapshot(gameTime, kills: 0, events: events);

    /// <summary>
    /// Mirrors the live payload's naming, which is the part that keeps breaking: the player row
    /// carries the full Riot ID while events use the bare name.
    /// </summary>
    private static GameSnapshot Snapshot(double gameTime, int kills, params GameEvent[] events) => new()
    {
        GameData = new GameStats { GameTime = gameTime, GameMode = "CLASSIC", MapName = "Map11" },
        ActivePlayer = new ActivePlayer { RiotIdGameName = "Oyuncu", CurrentGold = 1200 },
        AllPlayers =
        [
            new PlayerState
            {
                ChampionName = "Orianna",
                SummonerName = "Oyuncu#0001",
                RiotId = "Oyuncu#0001",
                RiotIdGameName = "Oyuncu",
                Position = "MIDDLE",
                Team = "ORDER",
                Level = 11,
                Scores = new PlayerScores { Kills = kills, Deaths = 2, Assists = 7, CreepScore = 140, WardScore = 18.5 },
            },
        ],
        Events = new EventContainer { Events = [.. events] },
    };
}
