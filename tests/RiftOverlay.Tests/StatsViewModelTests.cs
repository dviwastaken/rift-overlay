using RiftOverlay.Client;
using RiftOverlay.Services;

namespace RiftOverlay.Tests;

/// <summary>
/// The client's personal statistics section.
/// <para>
/// These rules moved here with the panel itself — they used to guard the overlay's history card,
/// which champion select and everything else pre-game left behind when it moved into the client.
/// The rules did not change: an unseen result is not a loss, and an unknown win rate is not zero.
/// </para>
/// </summary>
public sealed class StatsViewModelTests
{
    private static StatsViewModel ViewModel() =>
        new(new MatchDatabase(System.IO.Path.Combine(
            System.IO.Path.GetTempPath(), $"riftoverlay-stats-{Guid.NewGuid():N}.db")));

    private static MatchRecord Match(
        string champion, string? result, int kills = 1, int deaths = 1, int assists = 1, bool ended = true) =>
        new(1, DateTime.UtcNow, ended ? DateTime.UtcNow : null, "CLASSIC", "Map11", champion,
            "MIDDLE", "ORDER", result, 1694, kills, deaths, assists, 210, 22, 16);

    private static ChampionStat Stat(string champion, int matches, int decided, int wins, double? creepScoreAtTen) =>
        new(champion, matches, decided, wins, 5, 3, 8, 190, 20, creepScoreAtTen);

    [Fact]
    public void WithNoMatchesItSaysSoRatherThanShowingAnEmptyTable()
    {
        var viewModel = ViewModel();

        viewModel.Load([], []);

        Assert.False(viewModel.HasData);
        Assert.Contains("Henüz", viewModel.Status);
    }

    [Fact]
    public void AMatchStillInProgressIsNotCounted()
    {
        var viewModel = ViewModel();

        viewModel.Load([Match("Orianna", "Win"), Match("Ahri", null, ended: false)], []);

        Assert.Single(viewModel.Matches);
    }

    [Fact]
    public void AMatchWithNoSeenResultCountsAsPlayedButNotAsALoss()
    {
        var viewModel = ViewModel();

        viewModel.Load([Match("Orianna", "Win"), Match("Ahri", null)], []);

        // Two games recorded, one of them decided: the rate must read 100% of what is known, not
        // 50% of what is guessed.
        Assert.Equal(2, viewModel.Matches.Count);

        var played = Assert.Single(viewModel.Headlines, h => h.Label == "Kayıtlı maç");
        var rate = Assert.Single(viewModel.Headlines, h => h.Label == "Kazanma");

        Assert.Equal("2", played.Value);
        Assert.Contains("görülmedi", played.Detail);
        Assert.Equal("%100", rate.Value);
        Assert.Equal("1G / 0M", rate.Detail);
    }

    [Fact]
    public void WithNothingDecidedThereIsNoWinRateAtAll()
    {
        // Zero decided matches would divide by zero, and printing 0% would be a claim about games
        // whose outcome nobody ever saw.
        var viewModel = ViewModel();

        viewModel.Load([Match("Orianna", null)], []);

        var rate = Assert.Single(viewModel.Headlines, h => h.Label == "Kazanma");

        Assert.Equal("—", rate.Value);
    }

    [Fact]
    public void AChampionWithNoDecidedMatchesShowsADashRatherThanZero()
    {
        var viewModel = ViewModel();

        viewModel.Load([Match("Ahri", "Win")], [Stat("Ahri", matches: 2, decided: 0, wins: 0, creepScoreAtTen: null)]);

        var line = Assert.Single(viewModel.Champions);

        Assert.Equal("—", line.WinRate);
        Assert.Equal("—", line.CreepScoreAtTen);
    }

    [Fact]
    public void AChampionWithResultsShowsItsRateAndCreepScore()
    {
        var viewModel = ViewModel();

        viewModel.Load([Match("Orianna", "Win")], [Stat("Orianna", 12, 12, 7, 68.4)]);

        var line = Assert.Single(viewModel.Champions);

        Assert.Equal("12 maç", line.Played);
        Assert.Equal("%58", line.WinRate);
        Assert.Equal("68", line.CreepScoreAtTen);
    }

    [Fact]
    public void MatchRowsCarryTheirResultLetterAndWinFlag()
    {
        var viewModel = ViewModel();

        viewModel.Load([Match("Orianna", "Win"), Match("Ahri", "Lose"), Match("Lee Sin", null)], []);

        Assert.Equal(["G", "M", "—"], viewModel.Matches.Select(m => m.Result));
        Assert.Equal([true, false, false], viewModel.Matches.Select(m => m.Won));
    }

    [Fact]
    public void AChampionWithTooFewDecidedMatchesGetsNoColour()
    {
        // Three games and a colour is a verdict on a coin toss: one different result moves the
        // number by a third. The rate is still shown — it is the judgement that is withheld.
        var viewModel = ViewModel();

        viewModel.Load([Match("Ahri", "Win")], [Stat("Ahri", matches: 3, decided: 3, wins: 3, creepScoreAtTen: 60)]);

        var line = Assert.Single(viewModel.Champions);

        Assert.Equal("%100", line.WinRate);
        Assert.Null(line.AboveEven);
    }

    [Fact]
    public void AnExactlyEvenRecordIsNeitherGoodNorBad()
    {
        var viewModel = ViewModel();

        viewModel.Load([Match("Ahri", "Win")], [Stat("Ahri", matches: 10, decided: 10, wins: 5, creepScoreAtTen: 60)]);

        Assert.Null(Assert.Single(viewModel.Champions).AboveEven);
    }

    [Fact]
    public void APastTheFloorRecordIsJudgedAgainstAnEvenSplit()
    {
        // Fifty per cent is the right yardstick here and would be the wrong one on the build
        // screen: this is the player's own record, not a champion's average across everyone.
        var viewModel = ViewModel();

        viewModel.Load(
            [Match("Ahri", "Win"), Match("Ryze", "Lose")],
            [
                Stat("Ahri", matches: 12, decided: 12, wins: 8, creepScoreAtTen: 60),
                Stat("Ryze", matches: 12, decided: 12, wins: 4, creepScoreAtTen: 60),
            ]);

        Assert.True(viewModel.Champions.Single(c => c.Champion == "Ahri").AboveEven);
        Assert.False(viewModel.Champions.Single(c => c.Champion == "Ryze").AboveEven);
    }

    [Fact]
    public void ReloadingReplacesTheListRatherThanAppendingToIt()
    {
        var viewModel = ViewModel();

        viewModel.Load([Match("Orianna", "Win")], []);
        viewModel.Load([Match("Orianna", "Win")], []);

        Assert.Single(viewModel.Matches);
    }
}
