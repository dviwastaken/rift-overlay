using RiftOverlay.Services.Lcu;

namespace RiftOverlay.Tests;

/// <summary>
/// Rules for what a looked-up profile is allowed to claim.
/// <para>
/// The win-rate rule below came out of a live check, not a hunch: the client's ranked endpoint has
/// a <c>losses</c> field and never fills it in. A Silver I account returned 163 wins and 0 losses,
/// which the obvious formula turns into a confident 100% for every ranked player who has ever
/// existed.
/// </para>
/// </summary>
public sealed class SummonerLookupTests
{
    private static RankEntry Rank(int wins, int losses, string tier = "SILVER") =>
        new("RANKED_SOLO_5x5", tier, "I", 16, wins, losses);

    [Fact]
    public void WithNoReportedLosses_NoWinRateIsOffered()
    {
        // 163 and 0 is what the live endpoint actually returns. It means "losses not reported",
        // not "never lost", and the difference is the whole point.
        var rank = Rank(163, 0);

        Assert.Null(rank.WinRate);
        Assert.Null(rank.Games);
        Assert.Equal(163, rank.Wins);
    }

    [Fact]
    public void WithLossesReported_TheRateIsComputed()
    {
        var rank = Rank(60, 40);

        Assert.Equal(0.6, rank.WinRate);
        Assert.Equal(100, rank.Games);
    }

    [Fact]
    public void AGenuineUnbeatenRecordIsHiddenToo_AndThatIsTheTrade()
    {
        // A real 3-0 is indistinguishable from unreported losses, so it is hidden as well. Hiding
        // something true is recoverable; printing something false is not.
        Assert.Null(Rank(3, 0).WinRate);
    }

    [Theory]
    [InlineData("", false)]
    [InlineData("   ", false)]
    [InlineData("NONE", false)]
    [InlineData("none", false)]
    [InlineData("SILVER", true)]
    public void OnlyAnActualTierCountsAsRanked(string tier, bool expected) =>
        Assert.Equal(expected, Rank(1, 1, tier).IsRanked);

    [Fact]
    public void AProfileFormatsItsRiotIdBackTogether() =>
        Assert.Equal("Oyuncu#TR1", new PlayerProfile("Oyuncu", "TR1", 536, "p", [], []).RiotId);

    [Fact]
    public void CreepScoreIncludesJungleCamps()
    {
        // Lane minions and neutral monsters are reported separately; a jungler's CS is mostly the
        // second number, so summing them is not optional.
        var summary = new MatchSummary(1, "Lee Sin", "CLASSIC", true, 5, 3, 8, 30 + 140, 16,
            TimeSpan.FromMinutes(28), DateTime.Now);

        Assert.Equal(170, summary.CreepScore);
    }
}
