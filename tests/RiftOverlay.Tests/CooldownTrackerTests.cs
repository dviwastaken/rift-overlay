using RiftOverlay.Models;
using RiftOverlay.Services;

namespace RiftOverlay.Tests;

public class CooldownTrackerTests
{
    private static readonly SummonerSpellSettings Settings = new();

    [Fact]
    public void PlainFlash_UsesTheBaseCooldown()
    {
        var tracker = new CooldownTracker(Settings);

        var (cooldown, uncertain) = tracker.ComputeCooldown(Player(), "Flash");

        Assert.Equal(300, cooldown, precision: 3);
        Assert.False(uncertain);
    }

    [Fact]
    public void LucidityBoots_ShortenTheCooldown_AndAreCertain()
    {
        var tracker = new CooldownTracker(Settings);

        var (cooldown, uncertain) = tracker.ComputeCooldown(
            Player(items: [Settings.LucidityBootsItemId]), "Flash");

        Assert.Equal(300 * 100d / 110d, cooldown, precision: 3);
        Assert.False(uncertain);
    }

    /// <summary>
    /// Minor runes are invisible for enemies, so an Inspiration tree only tells us Cosmic Insight
    /// is possible. The timer takes the shorter, safer reading and flags it.
    /// </summary>
    [Fact]
    public void InspirationTree_AssumesCosmicInsight_AndFlagsUncertainty()
    {
        var tracker = new CooldownTracker(Settings);

        var (cooldown, uncertain) = tracker.ComputeCooldown(
            Player(secondaryTree: Settings.InspirationTreeId), "Flash");

        Assert.Equal(300 * 100d / 118d, cooldown, precision: 3);
        Assert.True(uncertain);
    }

    [Fact]
    public void BootsAndInspiration_Stack()
    {
        var tracker = new CooldownTracker(Settings);

        var (cooldown, _) = tracker.ComputeCooldown(
            Player(items: [Settings.LucidityBootsItemId], primaryTree: Settings.InspirationTreeId), "Flash");

        Assert.Equal(300 * 100d / 128d, cooldown, precision: 3);
    }

    [Fact]
    public void UnknownSpell_FallsBackToTheDefaultCooldown()
    {
        var tracker = new CooldownTracker(Settings);

        var (cooldown, _) = tracker.ComputeCooldown(Player(), "Bir Sonraki Yamadaki Spell");

        Assert.Equal(Settings.DefaultCooldown, cooldown);
    }

    [Fact]
    public void Toggle_StartsTheCountdown()
    {
        var tracker = new CooldownTracker(Settings);
        var player = Player();

        tracker.Toggle(player, SpellSlot.First, gameTime: 600);

        var cooldown = tracker.Get(player.SummonerName, SpellSlot.First, gameTime: 600);

        Assert.NotNull(cooldown);
        Assert.Equal("Flash", cooldown.SpellName);
        Assert.Equal(900, cooldown.ReadyAtGameTime, precision: 3);
    }

    [Fact]
    public void TogglingTwice_CancelsAMisPress()
    {
        var tracker = new CooldownTracker(Settings);
        var player = Player();

        tracker.Toggle(player, SpellSlot.First, gameTime: 600);
        tracker.Toggle(player, SpellSlot.First, gameTime: 602);

        Assert.Null(tracker.Get(player.SummonerName, SpellSlot.First, gameTime: 602));
    }

    [Fact]
    public void OnceTheCooldownElapses_TheSpellReadsAsReady()
    {
        var tracker = new CooldownTracker(Settings);
        var player = Player();

        tracker.Toggle(player, SpellSlot.First, gameTime: 600);

        Assert.NotNull(tracker.Get(player.SummonerName, SpellSlot.First, gameTime: 899));
        Assert.Null(tracker.Get(player.SummonerName, SpellSlot.First, gameTime: 900));
    }

    [Fact]
    public void TrackingIsPerSlot_NotPerPlayer()
    {
        var tracker = new CooldownTracker(Settings);
        var player = Player();

        tracker.Toggle(player, SpellSlot.First, gameTime: 600);

        Assert.NotNull(tracker.Get(player.SummonerName, SpellSlot.First, gameTime: 601));
        Assert.Null(tracker.Get(player.SummonerName, SpellSlot.Second, gameTime: 601));
    }

    [Fact]
    public void Reset_ClearsEverythingForTheNextGame()
    {
        var tracker = new CooldownTracker(Settings);
        var player = Player();

        tracker.Toggle(player, SpellSlot.First, gameTime: 600);
        tracker.Reset();

        Assert.Null(tracker.Get(player.SummonerName, SpellSlot.First, gameTime: 601));
    }

    [Fact]
    public void SpellName_ReadsTheRequestedSlot()
    {
        var player = Player();

        Assert.Equal("Flash", CooldownTracker.SpellName(player, SpellSlot.First));
        Assert.Equal("Teleport", CooldownTracker.SpellName(player, SpellSlot.Second));
    }

    private static PlayerState Player(
        int[]? items = null,
        int primaryTree = 8000,
        int secondaryTree = 8400) => new()
        {
            SummonerName = "Rakip1",
            ChampionName = "Renekton",
            Team = "CHAOS",
            Items = [.. (items ?? []).Select(id => new PlayerItem { ItemId = id })],
            Runes = new PlayerRunes
            {
                PrimaryRuneTree = new RuneInfo { Id = primaryTree },
                SecondaryRuneTree = new RuneInfo { Id = secondaryTree },
            },
            SummonerSpells = new SummonerSpells
            {
                SummonerSpellOne = new SummonerSpell { DisplayName = "Flash" },
                SummonerSpellTwo = new SummonerSpell { DisplayName = "Teleport" },
            },
        };
}
