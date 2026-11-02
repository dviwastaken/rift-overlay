using System.IO;
using RiftOverlay.Services;
using RiftOverlay.Services.Lcu;

namespace RiftOverlay.Tests;

/// <summary>
/// Pairing a recorded match with the client's own history so the result can be filled in.
/// <para>
/// The live API never delivers <c>GameEnd</c> before its endpoint closes with the game, so this is
/// the only path to a win or loss. It is also the only place in the project that decides two
/// records describe the same event without a shared id to join on — a wrong pairing writes the
/// wrong result and nothing downstream can tell.
/// </para>
/// </summary>
public sealed class MatchResultResolverTests : IDisposable
{
    private readonly string _path = Path.Combine(
        Path.GetTempPath(), $"riftoverlay-resolve-{Guid.NewGuid():N}.db");

    private MatchDatabase Database() => new(_path);

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();

        if (File.Exists(_path))
        {
            File.Delete(_path);
        }
    }

    private static int Record(MatchDatabase database, string champion, int kills, int deaths, int assists)
    {
        var id = database.StartMatch("CLASSIC", "Map11", champion, "MIDDLE", "ORDER");

        database.UpdateTotals(id, 1500, new MatchSample(1500, kills, deaths, assists, 200, 20, 16, 12000));
        database.EndMatch(id, result: null);

        return id;
    }

    [Fact]
    public void AFinishedMatchWithNoResultIsListedAsUnresolved()
    {
        var database = Database();
        var id = Record(database, "Aphelios", 18, 4, 8);

        Assert.Equal(id, Assert.Single(database.GetUnresolvedMatches()).Id);
    }

    [Fact]
    public void AMatchThatAlreadyHasAResultIsNotListed()
    {
        var database = Database();
        var id = database.StartMatch("CLASSIC", "Map11", "Ahri", "MIDDLE", "ORDER");

        database.EndMatch(id, "Win");

        Assert.Empty(database.GetUnresolvedMatches());
    }

    [Fact]
    public void AMatchStillInProgressIsNotListed()
    {
        // Unfinished is not unresolved — filling in a result for a game still being played would
        // close it early and freeze whatever the score happened to be.
        var database = Database();

        database.StartMatch("CLASSIC", "Map11", "Ahri", "MIDDLE", "ORDER");

        Assert.Empty(database.GetUnresolvedMatches());
    }

    [Fact]
    public void SettingAResultWorksOnceAndOnlyOnce()
    {
        var database = Database();
        var id = Record(database, "Aphelios", 18, 4, 8);

        Assert.True(database.SetResult(id, "Win"));

        // A result seen live is better evidence than one inferred afterwards, so a second write is
        // refused rather than allowed to overwrite it.
        Assert.False(database.SetResult(id, "Lose"));
        Assert.Equal("Win", Assert.Single(database.GetRecentMatches()).Result);
    }

    [Fact]
    public void OnceResolvedItLeavesTheUnresolvedList()
    {
        var database = Database();
        var id = Record(database, "Aphelios", 18, 4, 8);

        database.SetResult(id, "Lose");

        Assert.Empty(database.GetUnresolvedMatches());
    }

    [Fact]
    public void TheRecordedTotalsSurviveTheRoundTrip()
    {
        // The pairing rule leans on the scoreline, so it has to be stored exactly as observed.
        var database = Database();

        Record(database, "Aphelios", 18, 4, 8);

        var match = Assert.Single(database.GetUnresolvedMatches());

        Assert.Equal("Aphelios", match.Champion);
        Assert.Equal(18, match.Kills);
        Assert.Equal(4, match.Deaths);
        Assert.Equal(8, match.Assists);
    }

    [Fact]
    public void DeletingAMatchTakesItsEventsAndSamplesWithIt()
    {
        // Leaving those behind would orphan them against a match that no longer exists: invisible
        // in every screen, still sitting in the tables.
        var database = Database();
        var id = Record(database, "Corki", 0, 0, 0);

        database.SaveEvents(id, [new MatchEventRow(1, "GameStart", 0, "")]);
        database.SaveSample(id, new MatchSample(0, 0, 0, 0, 0, 0, 1, 500));

        Assert.True(database.DeleteMatch(id));

        Assert.Empty(database.GetRecentMatches());
        Assert.Empty(database.GetEvents(id));
        Assert.Empty(database.GetSamples(id));
    }

    [Fact]
    public void DeletingOneMatchLeavesTheOthersAlone()
    {
        var database = Database();
        var keep = Record(database, "Aphelios", 18, 4, 8);
        var drop = Record(database, "Corki", 0, 0, 0);

        database.DeleteMatch(drop);

        Assert.Equal(keep, Assert.Single(database.GetRecentMatches()).Id);
    }

    [Fact]
    public void DeletingSomethingThatIsNotThereIsNotAnError() =>
        Assert.False(Database().DeleteMatch(999));

    [Fact]
    public void SeveralUnresolvedMatchesComeBackNewestFirst()
    {
        var database = Database();

        Record(database, "Ahri", 1, 1, 1);
        Record(database, "Aphelios", 2, 2, 2);

        var pending = database.GetUnresolvedMatches();

        Assert.Equal(2, pending.Count);
        Assert.Equal("Aphelios", pending[0].Champion);
    }
}
