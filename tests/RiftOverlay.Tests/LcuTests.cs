using RiftOverlay.Client;
using RiftOverlay.Services.Lcu;
using RiftOverlay.ViewModels;

namespace RiftOverlay.Tests;

public class LcuTests
{
    /// <summary>Shape copied from a live lockfile; the password here is invented.</summary>
    [Fact]
    public void ALockfile_YieldsThePortAndPassword()
    {
        var credentials = LcuCredentials.Parse("LeagueClient:15524:63899:aBcDeFgHiJkLmNoPqRsTuV:https");

        Assert.NotNull(credentials);
        Assert.Equal(63899, credentials.Port);
        Assert.Equal("aBcDeFgHiJkLmNoPqRsTuV", credentials.Password);
    }

    [Fact]
    public void TrailingWhitespace_DoesNotBreakParsing()
    {
        Assert.Equal(63899, LcuCredentials.Parse("LeagueClient:1:63899:pw:https\n")!.Port);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("LeagueClient:15524:63899")]
    [InlineData("LeagueClient:15524:notaport:pw:https")]
    [InlineData("LeagueClient:15524:0:pw:https")]
    [InlineData("LeagueClient:15524:63899::https")]
    public void AnUnusableLockfile_YieldsNothing(string? text)
    {
        Assert.Null(LcuCredentials.Parse(text));
    }

    /// <summary>
    /// The session shape below is modelled from documentation rather than a live capture — champion
    /// select could not be entered while building this. These tests pin the interpretation so a
    /// real capture either confirms it or fails loudly.
    /// </summary>
    [Fact]
    public void EnemyPicks_AreReadFromTheirTeam()
    {
        var state = Describe(Session());

        Assert.Equal(["Ahri", "Lee Sin"], state.Enemies.Select(e => e.Champion));
        Assert.Equal("middle", state.Enemies[0].Position);
    }

    /// <summary>A hovered champion can still change, so locked and hovered must read differently.</summary>
    [Fact]
    public void AHoveredChampion_IsShownButNotMarkedLocked()
    {
        var state = Describe(Session());

        Assert.True(state.Enemies[0].IsLocked);
        Assert.False(state.Enemies[1].IsLocked);
        Assert.Equal("Lee Sin", state.Enemies[1].Champion);
    }

    [Fact]
    public void TheLocalPlayer_IsMarkedAmongAllies()
    {
        var state = Describe(Session());

        Assert.Single(state.Allies, a => a.IsSelf);
        Assert.Equal("Orianna", state.Allies.Single(a => a.IsSelf).Champion);
    }

    [Fact]
    public void OnlyCompletedEnemyBans_AreListed()
    {
        var state = Describe(Session());

        Assert.Equal(["Yasuo"], state.EnemyBans);
    }

    [Fact]
    public void TheTimer_IsReportedInSeconds()
    {
        Assert.Equal(27, Describe(Session()).SecondsLeft);
    }

    [Fact]
    public void AnUnknownChampionId_ShowsItsNumberRatherThanBlank()
    {
        var session = Session();
        session.TheirTeam[0].ChampionId = 9999;

        Assert.Equal("#9999", Describe(session).Enemies[0].Champion);
    }

    [Fact]
    public void AnEmptySlot_ReadsAsADash()
    {
        var session = Session();
        session.TheirTeam[0].ChampionId = 0;
        session.TheirTeam[0].ChampionPickIntent = 0;

        Assert.Equal("—", Describe(session).Enemies[0].Champion);
    }

    // Champion select is shown in the client now, so what it displays is asserted against the
    // client's own view model — see below. What stays here is the session parsing itself.

    [Fact]
    public void TheClientSection_ShowsBothTeamsTheClockAndTheBans()
    {
        var viewModel = new ChampSelectViewModel();

        viewModel.OnState(Describe(Session()));

        Assert.True(viewModel.InChampSelect);
        Assert.Equal(["Ahri", "Lee Sin"], viewModel.Enemies.Select(e => e.Champion));
        Assert.Equal(["Orianna"], viewModel.Allies.Select(a => a.Champion));
        Assert.Equal("0:27", viewModel.Clock);
        Assert.Contains("Yasuo", viewModel.Bans);
        Assert.True(viewModel.HasBans);
    }

    [Fact]
    public void LeavingChampSelect_ClearsTheSection()
    {
        var viewModel = new ChampSelectViewModel();
        viewModel.OnState(Describe(Session()));

        viewModel.OnState(null);

        Assert.False(viewModel.InChampSelect);
        Assert.Empty(viewModel.Enemies);
        Assert.Empty(viewModel.Allies);
        Assert.False(viewModel.HasBans);
    }

    [Fact]
    public void OutsideChampSelectTheSectionSaysWhereTheClientIs()
    {
        // Blank in a lobby, blank in a queue and blank with the client closed are three different
        // situations; the section names which one it is instead of showing nothing.
        var viewModel = new ChampSelectViewModel();

        viewModel.OnPhase("Matchmaking");
        Assert.Contains("Sıra", viewModel.Status);

        viewModel.OnPhase("None");
        Assert.Contains("kapalı", viewModel.Status);
    }

    [Fact]
    public void HoveringAndLockingAreShownDifferently()
    {
        // A hovered pick can still change. Treating it as settled is how a plan gets made against
        // a champion nobody ended up playing.
        var viewModel = new ChampSelectViewModel();

        viewModel.OnState(Describe(Session()));

        Assert.Equal("kilitli", viewModel.Enemies.Single(e => e.Champion == "Ahri").State);
        Assert.Equal("seçiyor", viewModel.Enemies.Single(e => e.Champion == "Lee Sin").State);
    }

    [Fact]
    public void WithoutARuneServiceSavingIsNotOffered()
    {
        // The button is hidden rather than shown and refused: there is nothing behind it.
        var viewModel = new ChampSelectViewModel();

        viewModel.OnState(Describe(Session()));

        Assert.False(viewModel.CanSaveRunes);
    }

    [Fact]
    public void TheBansField_IsPreferredOverTheActionList()
    {
        var session = Session();
        session.Bans.TheirTeamBans = [64];

        // The action list would have said Yasuo; the dedicated field wins.
        Assert.Equal(["Lee Sin"], Describe(session).EnemyBans);
    }

    [Fact]
    public void WithNoBansField_TheActionListStillAnswers()
    {
        // Practice Tool returns the field empty because it has no ban phase, and older shapes may
        // not carry it at all — either way the completed enemy ban actions are still readable.
        Assert.Equal(["Yasuo"], Describe(Session()).EnemyBans);
    }

    private static ChampSelectState Describe(ChampSelectSession session)
    {
        // Names normally arrive from the client; handed over directly so the test needs no client
        // and no reflection into the service.
        var champions = ChampionNames.From(new Dictionary<int, string>
        {
            [61] = "Orianna",
            [103] = "Ahri",
            [64] = "Lee Sin",
            [157] = "Yasuo",
        });

        var service = new ChampSelectService(new LcuClient(), champions);

        return service.Describe(session);
    }

    private static ChampSelectSession Session() => new()
    {
        LocalPlayerCellId = 0,
        Timer = new ChampSelectTimer { AdjustedTimeLeftInPhase = 27_400, Phase = "BAN_PICK" },
        MyTeam =
        [
            new ChampSelectPlayer { CellId = 0, ChampionId = 61, AssignedPosition = "middle" },
        ],
        TheirTeam =
        [
            new ChampSelectPlayer { CellId = 5, ChampionId = 103, AssignedPosition = "middle" },
            new ChampSelectPlayer { CellId = 6, ChampionPickIntent = 64, AssignedPosition = "jungle" },
        ],
        Actions =
        [
            [
                new ChampSelectAction { Id = 1, Type = "ban", ChampionId = 157, Completed = true, IsAllyAction = false },
                new ChampSelectAction { Id = 2, Type = "ban", ChampionId = 103, Completed = true, IsAllyAction = true },
                new ChampSelectAction { Id = 3, Type = "ban", ChampionId = 64, Completed = false, IsAllyAction = false },
            ],
        ],
    };
}
