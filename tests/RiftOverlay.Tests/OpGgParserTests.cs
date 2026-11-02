using System.IO;
using RiftOverlay.Services.OpGg;

namespace RiftOverlay.Tests;

/// <summary>
/// Parses a real captured OP.GG response. The fixture is a verbatim capture, not a hand-written
/// sample: every payload bug this project has hit came from inventing test data cleaner than
/// reality, so the file under <c>Fixtures/</c> is exactly what the server sent.
/// </summary>
public sealed class OpGgParserTests
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

    private static ChampionAnalysis Parse() => OpGgResponseParser.Parse(Fixture());

    [Fact]
    public void TheCapturedResponse_ParsesIntoTheModel()
    {
        // Champion id and damage type are facts about Orianna and stay put. The sample size is a
        // live figure that grows every time the fixture is recaptured, so it is asserted as
        // "present and plausible" rather than pinned to whatever it was on the capture day.
        var analysis = Parse();

        Assert.Equal(61, analysis.Summary.Id);
        Assert.Equal("AP", analysis.DamageType);
        Assert.True(analysis.Summary.AverageStats.Play > 1000);
        Assert.InRange(analysis.Summary.AverageStats.WinRate, 0.3, 0.7);
    }

    [Fact]
    public void ItemsThatShareAWireShape_LandInTheRightProperties()
    {
        // The whole reason the parser reads the class declarations: core items, boots, starting
        // items and summoner spells are all sent as "CoreItems" and are told apart only by their
        // position in the parent. Getting this wrong would file boots as the core build.
        var analysis = Parse();

        Assert.Equal([6655, 4645, 3157], analysis.CoreItems.Ids);
        Assert.Equal([3020], analysis.Boots.Ids);
        Assert.Equal([1056, 2003, 2003], analysis.StarterItems.Ids);
        Assert.Equal([4, 12], analysis.SummonerSpells.Ids);
    }

    [Fact]
    public void LocalisedNamesSurviveApostrophesAndTurkishLetters()
    {
        var analysis = Parse();

        // "Luden'in Feryadı" is one string; an apostrophe must not end it early.
        Assert.Equal(["Luden'in Feryadı", "Gölgealev", "Zhonya'nın Kumsaati"], analysis.CoreItems.Names);
        Assert.Equal(["Sihirbaz Pabuçları"], analysis.Boots.Names);
    }

    [Fact]
    public void SampleSizeIsCarriedOnEveryRow()
    {
        // The rule the whole panel rests on: no row may arrive without a count behind it.
        var analysis = Parse();

        Assert.True(analysis.CoreItems.Play > 0);
        Assert.True(analysis.Boots.Play > 0);
        Assert.True(analysis.StarterItems.Play > 0);
        Assert.True(analysis.Runes.Play > 0);
        Assert.True(analysis.Skills.Play > 0);
        Assert.True(analysis.SkillMasteries.Play > 0);

        // Wins can never exceed games; a row failing this is corrupt, not merely stale.
        Assert.True(analysis.CoreItems.Win <= analysis.CoreItems.Play);
        Assert.True(analysis.Runes.Win <= analysis.Runes.Play);
    }

    [Fact]
    public void WinRateIsNullWithoutGames_RatherThanZero()
    {
        // A row with no games has an unknown win rate, not a 0% one.
        Assert.Null(new BuildRow().WinRate);
        Assert.Equal(0.5, new BuildRow { Play = 10, Win = 5 }.WinRate);
    }

    [Fact]
    public void RunePagesComeThroughWithBothTreesAndShards()
    {
        var analysis = Parse();

        Assert.Equal(8200, analysis.Runes.PrimaryPageId);
        Assert.Equal("Büyücülük", analysis.Runes.PrimaryPageName);
        Assert.Equal([8214, 8226, 8210, 8237], analysis.Runes.PrimaryRuneIds);
        Assert.Equal("İsabet", analysis.Runes.SecondaryPageName);
        Assert.Equal([8009, 8014], analysis.Runes.SecondaryRuneIds);
        Assert.Equal([5005, 5008, 5001], analysis.Runes.StatModIds);
    }

    [Fact]
    public void SkillOrderAndMaxPriorityAreSeparate()
    {
        var analysis = Parse();

        Assert.Equal(15, analysis.Skills.Order.Count);
        Assert.Equal(["Q", "W", "E", "Q", "Q", "R"], analysis.Skills.Order.Take(6));
        Assert.Equal(["Q", "W", "E"], analysis.SkillMasteries.Ids);
    }

    [Fact]
    public void BothCounterListsArriveAndAreToldApartByPosition()
    {
        // Both lists come over the wire under the same class name, exactly like the item rows.
        // Only the parent's field order separates them, so filing one as the other is the failure
        // this checks for — and it would invert every matchup shown.
        var analysis = Parse();

        Assert.NotEmpty(analysis.StrongCounters);
        Assert.NotEmpty(analysis.WeakCounters);
        Assert.Empty(analysis.StrongCounters.Select(c => c.ChampionName)
            .Intersect(analysis.WeakCounters.Select(c => c.ChampionName)));
    }

    [Fact]
    public void MyWinRateMeansOurSideInBothLists()
    {
        // The plain win_rate switches sides between the two lists — the schema says so — which is
        // why the app reads my_win_rate instead. The check is arithmetic rather than a fixed
        // number: our wins over our games must equal our rate, whichever list the row came from.
        var analysis = Parse();

        foreach (var counter in analysis.StrongCounters.Concat(analysis.WeakCounters))
        {
            Assert.True(counter.Play > 0, $"{counter.ChampionName} örneklemsiz geldi.");
            Assert.Equal((double)counter.Win / counter.Play, counter.MyWinRate, 2);
        }
    }

    [Fact]
    public void AChampionWeBeatIsRatedAboveHalf_AndOneWeLoseToBelow()
    {
        var analysis = Parse();

        Assert.All(analysis.StrongCounters, c => Assert.True(c.MyWinRate > 0.5,
            $"{c.ChampionName} iyi eşleşme listesinde ama oranımız %50'nin altında."));

        Assert.All(analysis.WeakCounters, c => Assert.True(c.MyWinRate < 0.5,
            $"{c.ChampionName} zor eşleşme listesinde ama oranımız %50'nin üstünde."));
    }

    [Fact]
    public void BooleansAndNestedObjectsParse()
    {
        var analysis = Parse();

        Assert.False(analysis.Summary.IsRotation);
        Assert.False(analysis.Summary.IsRip);
        Assert.Equal(2.41, analysis.Summary.AverageStats.Kda, 2);
    }

    [Fact]
    public void AnUndeclaredClass_StillParsesWithPositionalNames()
    {
        // Losing one object's field names must not fail the whole response — a new field on the
        // server's side should degrade, not break.
        var json = OpGgResponseParser.ToJson("class Data: a\n\nData(Mystery(1,\"x\"))");

        Assert.Contains("\"field0\":1", json);
        Assert.Contains("\"field1\":\"x\"", json);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("class Data: a")]
    public void TextThatIsNotAResponse_IsRejectedRatherThanReturningNothing(string text)
    {
        // Silently yielding an empty object would look like "this champion has no build".
        Assert.Throws<FormatException>(() => OpGgResponseParser.ToJson(text));
    }
}
