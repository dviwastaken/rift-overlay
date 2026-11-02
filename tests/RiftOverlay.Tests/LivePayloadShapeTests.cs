using RiftOverlay.Models;
using RiftOverlay.Services;
using RiftOverlay.ViewModels;

namespace RiftOverlay.Tests;

/// <summary>
/// Regressions found by running tools/LiveApiProbe against a real match. Each of these passed
/// against invented fixtures and failed against the actual payload, so the shapes here are copied
/// from a live capture rather than made up.
/// </summary>
public class LivePayloadShapeTests
{
    /// <summary>
    /// The live player list puts the full Riot ID in <em>every</em> name field — summonerName is
    /// "Ornek#0001", not "Ornek" — while events name the killer as the bare "Ornek". Matching those
    /// directly never succeeded, so no dragon, void grub or buff was ever credited to a team.
    /// </summary>
    [Fact]
    public void EventKillerName_MatchesAPlayerWhoseNameFieldsCarryTheTag()
    {
        var tracker = new ObjectiveTracker { AllyTeam = "ORDER" };

        tracker.Process(new GameSnapshot
        {
            AllPlayers =
            [
                new PlayerState
                {
                    SummonerName = "Ornek#0001",
                    RiotId = "Ornek#0001",
                    RiotIdGameName = "Ornek",
                    Team = "ORDER",
                },
            ],
            Events = new EventContainer
            {
                Events =
                [
                    new GameEvent
                    {
                        EventId = 1,
                        EventName = "DragonKill",
                        EventTime = 400,
                        KillerName = "Ornek",
                        DragonType = "Infernal",
                    },
                ],
            },
        });

        Assert.Equal(1, tracker.OrderDragons);
    }

    /// <summary>
    /// Copied from a live capture. Two things had been guessed wrong: the prefix is Inhib_ rather
    /// than Barracks_, and the lane token is a <em>number</em> — the id below was the MIDDLE
    /// inhibitor, which rules out reading L/C/R as left, centre and right.
    /// <para>
    /// The numbering is zero-based from the bottom of the map — L0 bottom, L1 middle, L2 top — and
    /// all three come from live captures. There is no L3; a number outside 0–2 produces no lane
    /// word rather than a guessed one, so the side keeps reading and nothing false is asserted.
    /// </para>
    /// </summary>
    [Theory]
    [InlineData("Inhib_TChaos_L1_P1_1931666598_0", "rakipte · orta")]
    [InlineData("Inhib_TOrder_L1_P1_1931666598_0", "bizde · orta")]
    [InlineData("Inhib_TChaos_L2_P1_2351107073_0", "rakipte · üst")]
    [InlineData("Inhib_TOrder_L0_P1_2116220407_0", "bizde · alt")]
    [InlineData("Inhib_TChaos_L3_P1_1931666598_0", "rakipte")]
    [InlineData("Barracks_T2_L1", "rakipte · orta")]
    public void InhibitorIds_AreReadInBothNamingStyles(string inhibitorId, string expected)
    {
        var tracker = new ObjectiveTracker { AllyTeam = "ORDER" };

        tracker.Process(new GameSnapshot
        {
            Events = new EventContainer
            {
                Events =
                [
                    new GameEvent
                    {
                        EventId = 1,
                        EventName = "InhibKilled",
                        EventTime = 1400,
                        InhibKilled = inhibitorId,
                    },
                ],
            },
        });

        var inhib = tracker.GetObjectives(1410).Single(o => o.Kind == ObjectiveKind.Inhibitor);

        Assert.Equal(expected, inhib.Detail);
    }

    /// <summary>
    /// Riot runs limited modes on the Howling Abyss that are not ARAM — the capture was gameMode
    /// "KIWI" on Map12. Labelling from the map alone called that ARAM.
    /// </summary>
    [Fact]
    public void AnUnknownModeOnTheHowlingAbyss_IsNotCalledAram()
    {
        var viewModel = new OverlayViewModel();

        viewModel.OnSnapshot(Snapshot(gameMode: "KIWI", mapName: "Map12"));

        Assert.Equal("Bağlı · KIWI", viewModel.StatusText);
        Assert.DoesNotContain(viewModel.Objectives, o => o.Kind == ObjectiveKind.Dragon);
    }

    [Fact]
    public void ARealAramGame_IsStillCalledAram()
    {
        var viewModel = new OverlayViewModel();

        viewModel.OnSnapshot(Snapshot(gameMode: "ARAM", mapName: "Map12"));

        Assert.Equal("Bağlı · ARAM", viewModel.StatusText);
    }

    /// <summary>Both of these turned up in the capture and had no feed line.</summary>
    [Fact]
    public void MultikillAndFirstBrick_AreDescribed()
    {
        var viewModel = new OverlayViewModel();

        viewModel.OnSnapshot(Snapshot(events:
        [
            new GameEvent
            {
                EventId = 8,
                EventName = "Multikill",
                EventTime = 86,
                KillerName = "Ornek",
                KillStreak = 2,
            },
            new GameEvent { EventId = 66, EventName = "FirstBrick", EventTime = 478, KillerName = "Ornek" },
        ]));

        Assert.Contains("Ornek double kill", viewModel.Feed[1]);
        Assert.Contains("İlk kule — Ornek", viewModel.Feed[0]);
    }

    [Theory]
    [InlineData(2, "double kill")]
    [InlineData(3, "triple kill")]
    [InlineData(4, "quadra kill")]
    [InlineData(5, "penta kill")]
    public void MultikillStreaks_AreNamed(int streak, string expected)
    {
        var viewModel = new OverlayViewModel();

        viewModel.OnSnapshot(Snapshot(events:
        [
            new GameEvent
            {
                EventId = 1,
                EventName = "Multikill",
                EventTime = 100,
                KillerName = "Ornek",
                KillStreak = streak,
            },
        ]));

        Assert.Contains(expected, viewModel.Feed[0]);
    }

    /// <summary>
    /// The full event table was read out of the game binary, where the names sit alongside the
    /// payload field names (VictimName, KillStreak, DragonType, AcingTeam). These are the ones
    /// that had no feed line.
    /// </summary>
    [Theory]
    [InlineData("HordeKill", "Void grub — Ornek")]
    [InlineData("InhibRespawningSoon", "Inhibitor birazdan dönüyor")]
    [InlineData("InhibRespawned", "Inhibitor döndü")]
    [InlineData("GameEnd", "Oyun bitti")]
    public void EveryEventNameInTheGamesOwnTable_HasAFeedLine(string eventName, string expected)
    {
        var viewModel = new OverlayViewModel();

        viewModel.OnSnapshot(Snapshot(events:
        [
            new GameEvent { EventId = 1, EventName = eventName, EventTime = 400, KillerName = "Ornek" },
        ]));

        Assert.Contains(expected, viewModel.Feed[0]);
    }

    [Fact]
    public void AnAce_NamesWhoLandedIt()
    {
        var viewModel = new OverlayViewModel();

        viewModel.OnSnapshot(Snapshot(events:
        [
            new GameEvent { EventId = 1, EventName = "Ace", EventTime = 521, Acer = "Ornek", AcingTeam = "ORDER" },
        ]));

        Assert.Contains("Ace — Ornek", viewModel.Feed[0]);
    }

    /// <summary>
    /// A single live event mixed both naming styles: <c>{"KillerName":"Sion Bot","VictimName":"Oyuncu"}</c>.
    /// Bots are named the way <c>summonerName</c> spells them, humans the way
    /// <c>riotIdGameName</c> does, so there is no one field the feed always uses — which is why a
    /// bot taking an objective only gets credited if every name field is tried.
    /// </summary>
    [Theory]
    [InlineData("Sion Bot", "CHAOS")]
    [InlineData("Oyuncu", "ORDER")]
    public void BothBotAndHumanNaming_ResolveToATeam(string killerName, string expectedTeam)
    {
        var tracker = new ObjectiveTracker { AllyTeam = "ORDER" };

        tracker.Process(new GameSnapshot
        {
            AllPlayers =
            [
                new PlayerState
                {
                    SummonerName = "Oyuncu#TR1",
                    RiotId = "Oyuncu#TR1",
                    RiotIdGameName = "Oyuncu",
                    Team = "ORDER",
                },
                new PlayerState
                {
                    SummonerName = "Sion Bot",
                    RiotId = "Sion#BOT",
                    RiotIdGameName = "Sion",
                    IsBot = true,
                    Team = "CHAOS",
                },
            ],
            Events = new EventContainer
            {
                Events =
                [
                    new GameEvent
                    {
                        EventId = 1,
                        EventName = "DragonKill",
                        EventTime = 400,
                        KillerName = killerName,
                        DragonType = "Earth",
                    },
                ],
            },
        });

        var credited = expectedTeam == "ORDER" ? tracker.OrderDragons : tracker.ChaosDragons;
        var other = expectedTeam == "ORDER" ? tracker.ChaosDragons : tracker.OrderDragons;

        Assert.Equal(1, credited);
        Assert.Equal(0, other);
    }

    /// <summary>
    /// Every inhibitor event carries the structure id in a field named after the event, so an
    /// InhibRespawned payload has no InhibKilled at all. Reading the wrong one left respawned
    /// inhibitors sitting on the row until their own timer ran out.
    /// </summary>
    [Fact]
    public void InhibRespawned_ClearsTheCounterUsingItsOwnField()
    {
        var tracker = new ObjectiveTracker { AllyTeam = "ORDER" };
        const string inhibitor = "Inhib_TChaos_L1_P1_1931666598_0";

        tracker.Process(new GameSnapshot
        {
            Events = new EventContainer
            {
                Events =
                [
                    new GameEvent
                    {
                        EventId = 1,
                        EventName = "InhibKilled",
                        EventTime = 350,
                        InhibKilled = inhibitor,
                    },
                    new GameEvent
                    {
                        EventId = 2,
                        EventName = "InhibRespawned",
                        EventTime = 650,
                        InhibRespawned = inhibitor,
                    },
                ],
            },
        });

        Assert.DoesNotContain(tracker.GetObjectives(660), o => o.Kind == ObjectiveKind.Inhibitor);
    }

    /// <summary>
    /// "HordeKill" was a guess until the binary confirmed it, and a wrong name would have left the
    /// void grub score silently stuck at zero forever.
    /// </summary>
    [Fact]
    public void HordeKill_CreditsVoidGrubsToTheKillersTeam()
    {
        var tracker = new ObjectiveTracker { AllyTeam = "ORDER" };

        tracker.Process(new GameSnapshot
        {
            AllPlayers =
            [
                new PlayerState { SummonerName = "Ornek#0001", RiotIdGameName = "Ornek", Team = "ORDER" },
            ],
            Events = new EventContainer
            {
                Events =
                [
                    new GameEvent { EventId = 1, EventName = "HordeKill", EventTime = 400, KillerName = "Ornek" },
                ],
            },
        });

        Assert.Equal(1, tracker.OrderVoidgrubs);
    }

    private static GameSnapshot Snapshot(
        string gameMode = "CLASSIC",
        string mapName = "Map11",
        GameEvent[]? events = null) => new()
        {
            ActivePlayer = new ActivePlayer { SummonerName = "Oyuncu#TR1", RiotIdGameName = "Oyuncu" },
            AllPlayers =
            [
                new PlayerState
                {
                    SummonerName = "Oyuncu#TR1",
                    RiotId = "Oyuncu#TR1",
                    RiotIdGameName = "Oyuncu",
                    ChampionName = "Jarvan IV",
                    Team = "ORDER",
                },
                new PlayerState
                {
                    SummonerName = "Ornek#0001",
                    RiotId = "Ornek#0001",
                    RiotIdGameName = "Ornek",
                    ChampionName = "Trundle",
                    Team = "CHAOS",
                },
            ],
            Events = new EventContainer { Events = [.. events ?? []] },
            GameData = new GameStats { GameTime = 600, GameMode = gameMode, MapName = mapName },
        };
}
