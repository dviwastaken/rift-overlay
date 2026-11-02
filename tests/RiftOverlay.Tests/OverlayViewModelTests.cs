using RiftOverlay.Models;
using RiftOverlay.Services;
using RiftOverlay.ViewModels;

namespace RiftOverlay.Tests;

public class OverlayViewModelTests
{
    [Fact]
    public void EnemyList_ContainsOnlyTheTeamOppositeTheActivePlayer()
    {
        var viewModel = new OverlayViewModel();

        viewModel.OnSnapshot(Snapshot(activePlayer: "MidLaner"));

        Assert.Equal(["Lee Sin", "Ahri"], viewModel.Enemies.Select(e => e.Champion));
    }

    /// <summary>
    /// Spectating and replays leave activePlayer empty; the overlay must still render rather than
    /// throwing or showing all ten champions as enemies.
    /// </summary>
    [Fact]
    public void MissingActivePlayer_FallsBackToTreatingChaosAsTheEnemy()
    {
        var viewModel = new OverlayViewModel();

        viewModel.OnSnapshot(Snapshot(activePlayer: null));

        Assert.Equal(["Lee Sin", "Ahri"], viewModel.Enemies.Select(e => e.Champion));
    }

    [Fact]
    public void DeadEnemy_ShowsRespawnCountdown_LivingEnemyShowsAlive()
    {
        var viewModel = new OverlayViewModel();

        viewModel.OnSnapshot(Snapshot(activePlayer: "MidLaner", deadEnemy: "Lee Sin", respawnTimer: 28));

        var dead = viewModel.Enemies.Single(e => e.Champion == "Lee Sin");
        var alive = viewModel.Enemies.Single(e => e.Champion == "Ahri");

        Assert.True(dead.IsDead);
        Assert.Equal("00:28", dead.Remaining);
        Assert.False(alive.IsDead);
        Assert.Equal("canlı", alive.Remaining);
    }

    [Fact]
    public void GameClock_FormatsAsMinutesAndSeconds()
    {
        var viewModel = new OverlayViewModel();

        viewModel.OnSnapshot(Snapshot(activePlayer: "MidLaner", gameTime: 1345));

        Assert.Equal("22:25", viewModel.GameClock);
    }

    [Fact]
    public void Feed_ShowsNewestFirst_AndTranslatesEventNames()
    {
        var viewModel = new OverlayViewModel();

        viewModel.OnSnapshot(Snapshot(
            activePlayer: "MidLaner",
            gameTime: 800,
            events:
            [
                Event(0, "GameStart", 0),
                Event(1, "DragonKill", 400, killer: "Ormanci", dragonType: "Infernal"),
                Event(2, "ChampionKill", 780, killer: "MidLaner", victim: "Rakip3"),
            ]));

        Assert.Equal(
            ["13:00  MidLaner → Rakip3", "06:40  Dragon Infernal — Ormanci", "00:00  Oyun başladı"],
            viewModel.Feed);
    }

    [Fact]
    public void SwitchingOffAnObjective_RemovesItsCounter()
    {
        var settings = new OverlaySettings();
        var viewModel = new OverlayViewModel(overlaySettings: settings);

        viewModel.OnSnapshot(Snapshot(activePlayer: "MidLaner"));
        Assert.Contains(viewModel.Objectives, o => o.Kind == ObjectiveKind.Baron);

        settings.ShowBaron = false;
        viewModel.Tick();

        Assert.DoesNotContain(viewModel.Objectives, o => o.Kind == ObjectiveKind.Baron);
        Assert.Contains(viewModel.Objectives, o => o.Kind == ObjectiveKind.Dragon);
    }

    [Fact]
    public void LosingTheGame_ClearsEverything()
    {
        var viewModel = new OverlayViewModel();

        viewModel.OnSnapshot(Snapshot(activePlayer: "MidLaner"));
        viewModel.OnStateChanged(DataSourceState.Waiting);

        Assert.False(viewModel.HasGame);
        Assert.Empty(viewModel.Enemies);
        Assert.Empty(viewModel.Objectives);
        Assert.Empty(viewModel.Feed);
    }

    private static GameEvent Event(
        int id,
        string name,
        double time,
        string? killer = null,
        string? victim = null,
        string? dragonType = null) => new()
        {
            EventId = id,
            EventName = name,
            EventTime = time,
            KillerName = killer,
            VictimName = victim,
            DragonType = dragonType,
        };

    [Fact]
    public void OutsideTheRift_TheModeIsNamedAndRiftCountersDisappear()
    {
        var viewModel = new OverlayViewModel();

        viewModel.OnSnapshot(Snapshot(activePlayer: "MidLaner", mapName: "Map12", gameMode: "ARAM"));

        Assert.Equal("Bağlı · ARAM", viewModel.StatusText);
        Assert.DoesNotContain(viewModel.Objectives, o => o.Kind == ObjectiveKind.Dragon);
    }

    [Fact]
    public void OnANormalRiftGame_TheModeIsNotSpelledOut()
    {
        var viewModel = new OverlayViewModel();

        viewModel.OnSnapshot(Snapshot(activePlayer: "MidLaner"));

        Assert.Equal("Bağlı", viewModel.StatusText);
        Assert.Contains(viewModel.Objectives, o => o.Kind == ObjectiveKind.Dragon);
    }

    /// <summary>The practice tool is still the Rift, so every counter has to keep working there.</summary>
    [Fact]
    public void PracticeTool_KeepsTheRiftCounters_AndIsLabelled()
    {
        var viewModel = new OverlayViewModel();

        viewModel.OnSnapshot(Snapshot(activePlayer: "MidLaner", gameMode: "PRACTICETOOL"));

        Assert.Equal("Bağlı · Pratik", viewModel.StatusText);
        Assert.Contains(viewModel.Objectives, o => o.Kind == ObjectiveKind.Dragon);
    }

    private static GameSnapshot Snapshot(
        string? activePlayer,
        double gameTime = 600,
        string? deadEnemy = null,
        double respawnTimer = 0,
        GameEvent[]? events = null,
        string mapName = "Map11",
        string gameMode = "CLASSIC") => new()
        {
            ActivePlayer = activePlayer is null
                ? null
                : new ActivePlayer { SummonerName = activePlayer, RiotIdGameName = activePlayer },
            AllPlayers =
            [
                Player("Ormanci", "Viego", "ORDER"),
                Player("MidLaner", "Orianna", "ORDER"),
                Player("Rakip2", "Lee Sin", "CHAOS", deadEnemy == "Lee Sin" ? respawnTimer : 0),
                Player("Rakip3", "Ahri", "CHAOS", deadEnemy == "Ahri" ? respawnTimer : 0),
            ],
            Events = new EventContainer { Events = [.. events ?? []] },
            GameData = new GameStats { GameTime = gameTime, GameMode = gameMode, MapName = mapName },
        };

    /// <summary>
    /// Mirrors the live payload: every name field carries the full Riot ID, while event fields
    /// elsewhere use the bare game name.
    /// </summary>
    private static PlayerState Player(string name, string champion, string team, double respawn = 0) => new()
    {
        SummonerName = $"{name}#TR1",
        RiotId = $"{name}#TR1",
        RiotIdGameName = name,
        ChampionName = champion,
        Team = team,
        IsDead = respawn > 0,
        RespawnTimer = respawn,
    };
}
