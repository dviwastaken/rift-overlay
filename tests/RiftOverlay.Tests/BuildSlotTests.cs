using System.IO;
using RiftOverlay.Client;
using RiftOverlay.Services;
using RiftOverlay.Services.OpGg;

namespace RiftOverlay.Tests;

/// <summary>
/// The slot options, the levelling grid and the lane headline, exercised against the verbatim
/// capture rather than a hand-built analysis.
/// </summary>
public sealed class BuildSlotTests
{
    private static ChampionAnalysis Analysis()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "RiftOverlay.slnx")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);

        return OpGgResponseParser.Parse(File.ReadAllText(Path.Combine(
            directory!.FullName, "tests", "RiftOverlay.Tests", "Fixtures", "opgg-orianna-full.txt")));
    }

    private static BuildViewModel ViewModel() =>
        new(new OpGgClient(), new SummonerSpellCatalog());

    private static BuildViewModel Loaded()
    {
        var viewModel = ViewModel();
        viewModel.Show(Analysis());
        return viewModel;
    }

    [Fact]
    public void EverySlotOptionCarriesItsSampleSize()
    {
        // The rule the whole project runs on: no rate is ever shown without the count behind it.
        // These rows are where it bites hardest — the sixth-item options in this capture rest on
        // single-digit games.
        var viewModel = Loaded();

        Assert.NotEmpty(viewModel.Slots);

        foreach (var option in viewModel.Slots.SelectMany(slot => slot.Options))
        {
            Assert.False(string.IsNullOrWhiteSpace(option.Sample));
            Assert.Contains("maç", option.Sample);
        }
    }

    [Fact]
    public void TheThinSixthSlotIsFlaggedAndTheFourthIsNot()
    {
        var viewModel = Loaded();

        var fourth = viewModel.Slots.Single(slot => slot.Label.StartsWith('4'));
        var sixth = viewModel.Slots.Single(slot => slot.Label.StartsWith('6'));

        Assert.False(fourth.Options[0].IsThin);
        Assert.True(sixth.Options[0].IsThin);
    }

    [Fact]
    public void SlotOptionsAreNamedRatherThanNumbered()
    {
        // Ids are the fallback, not the display: a row reading "3089" is a row nobody can act on.
        var viewModel = Loaded();

        var option = viewModel.Slots[0].Options[0];

        Assert.False(string.IsNullOrWhiteSpace(option.Name));
        Assert.False(int.TryParse(option.Name, out _));
    }

    [Fact]
    public void TheLevellingGridNumbersItsOwnCells()
    {
        // Fifteen steps arrive for a champion that levels eighteen times, so the numbers are carried
        // rather than taken from the view's index — the day the server returns the full order, an
        // index-numbered grid would start lying without anything failing.
        var viewModel = Loaded();

        Assert.True(viewModel.HasSkillOrder);
        Assert.Equal([1, 2, 3], viewModel.SkillOrder.Take(3).Select(step => step.Level));
        Assert.Equal(viewModel.SkillOrder.Count, viewModel.SkillOrder[^1].Level);
        Assert.All(viewModel.SkillOrder, step => Assert.Contains(step.Ability, new[] { "Q", "W", "E", "R" }));
    }

    [Fact]
    public void TheUltimateIsMarkedSoTheGridReadsAtAGlance()
    {
        var viewModel = Loaded();

        Assert.Contains(viewModel.SkillOrder, step => step.IsUltimate);
        Assert.All(
            viewModel.SkillOrder.Where(step => step.IsUltimate),
            step => Assert.Equal("R", step.Ability));
    }

    [Fact]
    public void TheGridPrefersTheMostPlayedAlternativeOverThePlainOrder()
    {
        var viewModel = Loaded();
        var analysis = Analysis();

        Assert.NotEmpty(analysis.SkillMasteries.Builds);
        Assert.Equal(
            analysis.SkillMasteries.Builds[0].Order.Select(ability => ability.ToUpperInvariant()),
            viewModel.SkillOrder.Select(step => step.Ability));
    }

    [Fact]
    public void TheHeadlineUsesTheLaneRateRatherThanTheBlendedAverage()
    {
        var viewModel = Loaded();

        Assert.True(viewModel.HasTier);
        Assert.Contains("kademe", viewModel.TierLabel);
        Assert.Contains("maç", viewModel.PositionRate);
        Assert.Equal("Sihirsel hasar", viewModel.DamageLabel);
    }

    [Fact]
    public void WithNoPositionBlockTheHeadlineStaysEmptyRatherThanBorrowingTheAverage()
    {
        // An absent lane block is a reason to show less. Falling back to the blended average would
        // put the answer to a different question under a lane-specific label.
        var analysis = Analysis();
        analysis.Summary.Positions.Clear();

        var viewModel = ViewModel();
        viewModel.Show(analysis);

        Assert.False(viewModel.HasTier);
        Assert.Equal(string.Empty, viewModel.TierLabel);
        Assert.Equal(string.Empty, viewModel.PositionRate);
    }

    [Fact]
    public void LastItemsNeverReachesTheInterface()
    {
        // Parsed, and deliberately unshown: its samples dwarf the core build and overlap it only
        // partly, so what one of its rows denotes is a guess. Three slots are displayed, no more.
        var analysis = Analysis();
        var viewModel = ViewModel();
        viewModel.Show(analysis);

        Assert.NotEmpty(analysis.LastItems);
        Assert.Equal(3, viewModel.Slots.Count);

        var shown = viewModel.Slots.SelectMany(slot => slot.Options).Select(option => option.Sample).ToList();
        Assert.DoesNotContain($"{analysis.LastItems[0].Play:N0} maç", shown);
    }

    [Fact]
    public void ReloadingReplacesTheSlotsRatherThanStackingThem()
    {
        var viewModel = Loaded();
        var first = viewModel.Slots.Count;

        viewModel.Show(Analysis());

        Assert.Equal(first, viewModel.Slots.Count);
        Assert.Equal(viewModel.SkillOrder.Count, Analysis().SkillMasteries.Builds[0].Order.Count);
    }
}
