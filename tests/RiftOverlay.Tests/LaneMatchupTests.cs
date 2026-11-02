using RiftOverlay.Models;
using RiftOverlay.Services;
using RiftOverlay.ViewModels;

namespace RiftOverlay.Tests;

public class LaneMatchupTests
{
    [Fact]
    public void WithPositionsOnBothSides_TheOpponentIsMatchedAutomatically()
    {
        var resolver = new LaneMatchupResolver();

        var comparison = resolver.Resolve(
            Player("Oyuncu", "Orianna", "MIDDLE", creepScore: 87, level: 11),
            [
                Player("Rakip1", "Ahri", "MIDDLE", creepScore: 94, level: 11),
                Player("Rakip2", "Lee Sin", "JUNGLE", creepScore: 40, level: 10),
            ]);

        Assert.NotNull(comparison);
        Assert.Equal("Ahri", comparison.Opponent.ChampionName);
        Assert.False(comparison.IsManual);
    }

    [Fact]
    public void TheDifferences_AreSignedFromThePlayersPointOfView()
    {
        var resolver = new LaneMatchupResolver();

        var comparison = resolver.Resolve(
            Player("Oyuncu", "Orianna", "MIDDLE", creepScore: 87, level: 11),
            [Player("Rakip1", "Ahri", "MIDDLE", creepScore: 94, level: 10)])!;

        Assert.Equal(-7, comparison.CreepScoreDifference);
        Assert.Equal(1, comparison.LevelDifference);
    }

    /// <summary>
    /// The live capture is the reason this feature needs a fallback at all: every bot had a real
    /// lane while the human player's position came back as NONE.
    /// </summary>
    [Fact]
    public void WithoutAPosition_ThereIsNoAutomaticMatch()
    {
        var resolver = new LaneMatchupResolver();

        var comparison = resolver.Resolve(
            Player("Oyuncu", "Akali", "NONE", creepScore: 90, level: 18),
            [Player("Rakip1", "Sion", "MIDDLE", creepScore: 20, level: 12)]);

        Assert.Null(comparison);
    }

    [Fact]
    public void AManualPick_WorksEvenWithNoPositionAtAll()
    {
        var resolver = new LaneMatchupResolver { ManualOpponent = "Rakip1#TR1" };

        var comparison = resolver.Resolve(
            Player("Oyuncu", "Akali", "NONE", creepScore: 90, level: 18),
            [Player("Rakip1", "Sion", "NONE", creepScore: 20, level: 12)]);

        Assert.NotNull(comparison);
        Assert.Equal("Sion", comparison.Opponent.ChampionName);
        Assert.True(comparison.IsManual);
        Assert.Equal(70, comparison.CreepScoreDifference);
    }

    [Fact]
    public void AManualPick_OverridesAWorkingPositionMatch()
    {
        var resolver = new LaneMatchupResolver { ManualOpponent = "Rakip2#TR1" };

        var comparison = resolver.Resolve(
            Player("Oyuncu", "Orianna", "MIDDLE", creepScore: 87, level: 11),
            [
                Player("Rakip1", "Ahri", "MIDDLE", creepScore: 94, level: 11),
                Player("Rakip2", "Lee Sin", "JUNGLE", creepScore: 40, level: 10),
            ])!;

        Assert.Equal("Lee Sin", comparison.Opponent.ChampionName);
        Assert.True(comparison.IsManual);
    }

    [Fact]
    public void ClearingTheManualPick_ReturnsToMatchingOnPosition()
    {
        var resolver = new LaneMatchupResolver { ManualOpponent = "Rakip2#TR1" };
        resolver.ManualOpponent = null;

        var comparison = resolver.Resolve(
            Player("Oyuncu", "Orianna", "MIDDLE", creepScore: 87, level: 11),
            [
                Player("Rakip1", "Ahri", "MIDDLE", creepScore: 94, level: 11),
                Player("Rakip2", "Lee Sin", "JUNGLE", creepScore: 40, level: 10),
            ])!;

        Assert.Equal("Ahri", comparison.Opponent.ChampionName);
    }

    /// <summary>A pick can point at someone who left the player list; that must not throw.</summary>
    [Fact]
    public void AManualPickThatNoLongerExists_FallsBackToPosition()
    {
        var resolver = new LaneMatchupResolver { ManualOpponent = "Gitti#TR1" };

        var comparison = resolver.Resolve(
            Player("Oyuncu", "Orianna", "MIDDLE", creepScore: 87, level: 11),
            [Player("Rakip1", "Ahri", "MIDDLE", creepScore: 94, level: 11)])!;

        Assert.Equal("Ahri", comparison.Opponent.ChampionName);
        Assert.False(comparison.IsManual);
    }

    [Fact]
    public void NoEnemies_ProducesNothing()
    {
        Assert.Null(new LaneMatchupResolver().Resolve(
            Player("Oyuncu", "Orianna", "MIDDLE", creepScore: 87, level: 11), []));
    }

    [Fact]
    public void WhenNoPositionMatches_ThereIsNoComparison()
    {
        var comparison = new LaneMatchupResolver().Resolve(
            Player("Oyuncu", "Orianna", "MIDDLE", creepScore: 87, level: 11),
            [Player("Rakip1", "Lee Sin", "JUNGLE", creepScore: 40, level: 10)]);

        Assert.Null(comparison);
    }

    [Fact]
    public void TheViewModel_FormatsTheDifferenceWithItsSign()
    {
        var viewModel = new OverlayViewModel();

        viewModel.OnSnapshot(Snapshot());

        Assert.True(viewModel.Lane.HasOpponent);
        Assert.Equal("Ahri", viewModel.Lane.Opponent);
        Assert.Equal("-7", viewModel.Lane.CreepScoreDifference);
        Assert.Equal("=", viewModel.Lane.LevelDifference);
        Assert.Equal(-7, viewModel.Lane.CreepScoreLead);
    }

    [Fact]
    public void ThePicker_OffersAutomaticPlusEveryEnemy()
    {
        var viewModel = new OverlayViewModel();

        viewModel.OnSnapshot(Snapshot());

        Assert.Equal(["Otomatik", "Ahri", "Lee Sin"], viewModel.LaneCandidates.Select(c => c.Name));
        Assert.True(viewModel.LaneCandidates[0].IsSelected);
    }

    [Fact]
    public void PickingAnOpponent_SwitchesTheComparisonAndMarksItManual()
    {
        var viewModel = new OverlayViewModel();
        viewModel.OnSnapshot(Snapshot());

        viewModel.SelectLaneOpponent("Rakip2#TR1");

        Assert.Equal("Lee Sin", viewModel.Lane.Opponent);
        Assert.Equal("elle seçildi", viewModel.Lane.Detail);
        Assert.True(viewModel.LaneCandidates.Single(c => c.Name == "Lee Sin").IsSelected);
    }

    [Fact]
    public void AMatchEnding_ForgetsTheManualPick()
    {
        var viewModel = new OverlayViewModel();
        viewModel.OnSnapshot(Snapshot());
        viewModel.SelectLaneOpponent("Rakip2#TR1");

        viewModel.OnStateChanged(DataSourceState.Waiting);
        viewModel.OnSnapshot(Snapshot());

        Assert.Equal("Ahri", viewModel.Lane.Opponent);
    }

    private static PlayerState Player(
        string name, string champion, string position, int creepScore, int level) => new()
        {
            SummonerName = $"{name}#TR1",
            RiotId = $"{name}#TR1",
            RiotIdGameName = name,
            ChampionName = champion,
            Position = position,
            Level = level,
            Team = name == "Oyuncu" ? "ORDER" : "CHAOS",
            Scores = new PlayerScores { CreepScore = creepScore },
        };

    private static GameSnapshot Snapshot() => new()
    {
        ActivePlayer = new ActivePlayer { SummonerName = "Oyuncu#TR1", RiotIdGameName = "Oyuncu" },
        AllPlayers =
        [
            Player("Oyuncu", "Orianna", "MIDDLE", 87, 11),
            Player("Rakip1", "Ahri", "MIDDLE", 94, 11),
            Player("Rakip2", "Lee Sin", "JUNGLE", 40, 10),
        ],
        GameData = new GameStats { GameTime = 600, GameMode = "CLASSIC", MapName = "Map11" },
    };
}
