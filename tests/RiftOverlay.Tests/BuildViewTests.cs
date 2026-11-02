using System.IO;
using RiftOverlay.Client;
using RiftOverlay.Services;
using RiftOverlay.Services.OpGg;

namespace RiftOverlay.Tests;

public sealed class BuildViewTests
{
    private static string Fixture()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "RiftOverlay.slnx")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);

        return File.ReadAllText(Path.Combine(
            directory!.FullName, "tests", "RiftOverlay.Tests", "Fixtures", "opgg-orianna.txt"));
    }

    /// <summary>
    /// Riot keys this file by the string id while the number everything else uses hides in a
    /// <c>key</c> field inside each entry. Indexing by the outer name builds a lookup that never
    /// matches, and the symptom is subtle: spells keep rendering as bare numbers.
    /// </summary>
    [Fact]
    public void SpellNamesAreIndexedByTheInnerKey_NotTheOuterName()
    {
        var catalog = new SummonerSpellCatalog();

        catalog.Parse("""
            {"type":"summoner","data":{
              "SummonerFlash":{"id":"SummonerFlash","name":"Sıçra","key":"4"},
              "SummonerTeleport":{"id":"SummonerTeleport","name":"Işınlan","key":"12"}
            }}
            """);

        Assert.True(catalog.IsReady);
        Assert.Equal("Sıçra", catalog.NameOf(4));
        Assert.Equal("Işınlan", catalog.NameOf(12));
    }

    [Fact]
    public void AnUnknownSpellFallsBackToItsId()
    {
        // Better a number than a blank: the row still says *something* stable, and a missing name
        // is visibly a missing name rather than a missing spell.
        Assert.Equal("99", new SummonerSpellCatalog().NameOf(99));
    }

    private static BuildViewModel ViewModel()
    {
        var spells = new SummonerSpellCatalog();

        spells.Parse("""
            {"type":"summoner","data":{
              "SummonerFlash":{"id":"SummonerFlash","name":"Sıçra","key":"4"},
              "SummonerTeleport":{"id":"SummonerTeleport","name":"Işınlan","key":"12"}
            }}
            """);

        return new BuildViewModel(new OpGgClient(), spells);
    }

    [Fact]
    public void EveryRowCarriesItsSampleSize()
    {
        // The single rule the report set for this panel. A row without a count is the failure.
        var viewModel = ViewModel();

        viewModel.Show(OpGgResponseParser.Parse(Fixture()));

        Assert.NotEmpty(viewModel.Lines);
        Assert.All(viewModel.Lines, line => Assert.EndsWith("maç", line.Sample));
        Assert.All(viewModel.Lines, line => Assert.NotEqual("0 maç", line.Sample));
    }

    [Fact]
    public void SpellIdsAreResolvedToNames()
    {
        var viewModel = ViewModel();

        viewModel.Show(OpGgResponseParser.Parse(Fixture()));

        var spells = Assert.Single(viewModel.Lines, l => l.Label == "Büyüler");
        Assert.Equal("Sıçra + Işınlan", spells.Value);
    }

    [Fact]
    public void ARowBelowTheThresholdIsFlaggedAsThin()
    {
        var viewModel = ViewModel();

        viewModel.Show(new ChampionAnalysis
        {
            CoreItems = new BuildRow { Names = ["Bir eşya"], Play = 6, Win = 5 },
        });

        // Six games and an 83% win rate: exactly the shape of the Challenger capture that set the
        // threshold. It must reach the reader marked, not as advice.
        var row = Assert.Single(viewModel.Lines);
        Assert.True(row.IsThin);
        Assert.Equal("6 maç", row.Sample);
        Assert.Equal("%83,3", row.Rate);
    }

    [Fact]
    public void AWellSampledRowIsNotFlagged()
    {
        var viewModel = ViewModel();

        viewModel.Show(new ChampionAnalysis
        {
            CoreItems = new BuildRow { Names = ["Bir eşya"], Play = 2303, Win = 1211 },
        });

        Assert.False(Assert.Single(viewModel.Lines).IsThin);
    }

    [Fact]
    public void AnEmptyRowIsSkippedRatherThanShownBlank()
    {
        var viewModel = ViewModel();

        viewModel.Show(new ChampionAnalysis());

        Assert.Empty(viewModel.Lines);
        Assert.False(viewModel.HasBuild);
    }

    [Fact]
    public async Task FetchingWithNoChampionDoesNothing()
    {
        var viewModel = ViewModel();

        await viewModel.LoadAsync();

        Assert.False(viewModel.IsBusy);
        Assert.Empty(viewModel.Lines);
    }
}
