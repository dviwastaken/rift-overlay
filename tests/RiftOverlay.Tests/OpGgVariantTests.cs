using System.IO;
using RiftOverlay.Client;
using RiftOverlay.Services.OpGg;

namespace RiftOverlay.Tests;

/// <summary>
/// The slot-by-slot item options, the alternative levelling orders and the per-position summary.
/// <para>
/// Like the other OP.GG fixture this one is a verbatim capture, and the field list it was captured
/// with is read out of <see cref="OpGgClient"/> itself rather than retyped. That matters here more
/// than usual: the server returns fields in <em>its</em> declared order, not the order they were
/// asked for, so a fixture recorded against a different request would exercise a layout the app
/// never actually receives.
/// </para>
/// </summary>
public sealed class OpGgVariantTests
{
    private static ChampionAnalysis Parse()
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

    [Fact]
    public void EachItemSlotLandsInItsOwnListRatherThanTheCoreBuild()
    {
        // Every one of these arrives on the wire as "CoreItems(...)" — the same class name as the
        // core build, the boots and the starters. Only the parent's declared field order tells them
        // apart, so this test is the guard against a fifth-item option being read as the core.
        var build = Parse();

        Assert.Equal(3, build.CoreItems.Ids.Count);
        Assert.NotEmpty(build.FourthItems);
        Assert.NotEmpty(build.FifthItems);
        Assert.NotEmpty(build.SixthItems);

        Assert.All(build.FourthItems, row => Assert.NotEmpty(row.Ids));
        Assert.All(build.FourthItems, row => Assert.NotEmpty(row.Names));
    }

    [Fact]
    public void SamplesFallOffSharplyDownTheSlots()
    {
        // Not a curiosity: the capture behind this fixture has single-digit games on the most
        // popular sixth item. Anything that renders these rows has to say so.
        var build = Parse();

        Assert.True(build.FourthItems[0].Play > build.FifthItems[0].Play);
        Assert.True(build.FifthItems[0].Play > build.SixthItems[0].Play);
        Assert.True(build.SixthItems[0].Play < BuildViewModel.ThinSample);
    }

    [Fact]
    public void AlternativeLevellingOrdersArriveWithTheMaxPriority()
    {
        var build = Parse();

        // The priority itself is three entries; each alternative is a full fifteen-level order.
        Assert.Equal(["Q", "W", "E"], build.SkillMasteries.Ids);
        Assert.True(build.SkillMasteries.Builds.Count > 1);
        Assert.All(build.SkillMasteries.Builds, row => Assert.Equal(15, row.Order.Count));

        // Most played first, which is what lets the view show the first one as the default.
        Assert.True(build.SkillMasteries.Builds[0].Play >= build.SkillMasteries.Builds[1].Play);
    }

    [Fact]
    public void ThePositionSummaryCarriesItsOwnRateAndTier()
    {
        // The blended average covers every lane a champion is played in. A mid pick that also sees
        // jungle play carries an average belonging to neither, so the position block is the honest
        // number to show next to a mid build.
        var build = Parse();

        var position = Assert.Single(build.Summary.Positions);

        Assert.Equal("MID", position.Name);
        Assert.True(position.Stats.Play > 0);
        Assert.InRange(position.Stats.WinRate, 0.3, 0.7);
        Assert.InRange(position.Stats.RoleRate, 0.0, 1.0);
        Assert.NotNull(position.Stats.TierData);
        Assert.True(position.Stats.TierData!.Tier > 0);
    }

    [Fact]
    public void LastItemsIsParsedButItsMeaningIsNotAssumed()
    {
        // It comes back with samples several times larger than the core build and only partly
        // overlapping it. Until that is understood it stays out of the interface: a slot label
        // guessed wrong is worse than a slot not shown.
        var build = Parse();

        Assert.NotEmpty(build.LastItems);
        Assert.True(build.LastItems[0].Play > build.CoreItems.Play);
    }
}
