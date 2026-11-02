using RiftOverlay.Models;
using RiftOverlay.Services;
using RiftOverlay.ViewModels;

namespace RiftOverlay.Tests;

public class ItemWatcherTests
{
    private const int LongSword = 1036;
    private const int Sheen = 3057;
    private const int TrinityForce = 3078;
    private const int CheapFinished = 4001;

    private static readonly FakeCatalog Catalog = new(
    [
        new ItemInfo(LongSword, "Uzun Kılıç", 350, [], false),
        new ItemInfo(Sheen, "Pırıltı", 900, [LongSword], false),
        new ItemInfo(TrinityForce, "Üçlü Kuvvet", 3333, [Sheen], true),
        new ItemInfo(CheapFinished, "Ucuz Bitmiş Item", 1100, [LongSword], true),
    ]);

    /// <summary>
    /// Attaching to a game already in progress must not announce six items per enemy that were
    /// bought minutes ago — the first sighting only records what they already have.
    /// </summary>
    [Fact]
    public void TheFirstSightingOfAPlayer_ReportsNothing()
    {
        var watcher = new ItemWatcher(Catalog);

        var completions = watcher.Observe([Enemy("Rakip1", TrinityForce)], gameTime: 600);

        Assert.Empty(completions);
    }

    [Fact]
    public void AnItemFinishedAfterwards_IsReported()
    {
        var watcher = new ItemWatcher(Catalog);

        watcher.Observe([Enemy("Rakip1", Sheen)], 600);
        var completions = watcher.Observe([Enemy("Rakip1", Sheen, TrinityForce)], 640);

        var completion = Assert.Single(completions);
        Assert.Equal("Üçlü Kuvvet", completion.ItemName);
        Assert.Equal("Renekton", completion.Champion);
        Assert.Equal(3333, completion.Cost);
        Assert.Equal(640, completion.GameTime);
    }

    [Fact]
    public void TheSameItem_IsReportedOnlyOnce()
    {
        var watcher = new ItemWatcher(Catalog);

        watcher.Observe([Enemy("Rakip1")], 600);
        watcher.Observe([Enemy("Rakip1", TrinityForce)], 640);
        var again = watcher.Observe([Enemy("Rakip1", TrinityForce)], 680);

        Assert.Empty(again);
    }

    /// <summary>Components are bought constantly; only the finished item is the power step.</summary>
    [Fact]
    public void Components_AreNotReported()
    {
        var watcher = new ItemWatcher(Catalog);

        watcher.Observe([Enemy("Rakip1")], 600);
        var completions = watcher.Observe([Enemy("Rakip1", Sheen, LongSword)], 640);

        Assert.Empty(completions);
    }

    /// <summary>
    /// Significance comes from the item's own cost rather than a curated list, which would need
    /// revisiting every patch and would quietly go stale.
    /// </summary>
    [Fact]
    public void AFinishedItemUnderTheCostThreshold_IsNotReported()
    {
        var watcher = new ItemWatcher(Catalog, minimumCost: 2500);

        watcher.Observe([Enemy("Rakip1")], 600);
        var completions = watcher.Observe([Enemy("Rakip1", CheapFinished)], 640);

        Assert.Empty(completions);
    }

    [Fact]
    public void LoweringTheThreshold_LetsTheCheaperItemThrough()
    {
        var watcher = new ItemWatcher(Catalog, minimumCost: 1000);

        watcher.Observe([Enemy("Rakip1")], 600);
        var completions = watcher.Observe([Enemy("Rakip1", CheapFinished)], 640);

        Assert.Single(completions);
    }

    [Fact]
    public void ASoldItem_DoesNotCountAsACompletionWhenReBought()
    {
        var watcher = new ItemWatcher(Catalog);

        watcher.Observe([Enemy("Rakip1", TrinityForce)], 600);
        watcher.Observe([Enemy("Rakip1")], 640);
        var reBought = watcher.Observe([Enemy("Rakip1", TrinityForce)], 680);

        // Selling then re-buying is a genuine second completion, so this one is reported.
        Assert.Single(reBought);
    }

    [Fact]
    public void EachPlayer_IsTrackedSeparately()
    {
        var watcher = new ItemWatcher(Catalog);

        watcher.Observe([Enemy("Rakip1"), Enemy("Rakip2")], 600);
        var completions = watcher.Observe([Enemy("Rakip1", TrinityForce), Enemy("Rakip2")], 640);

        Assert.Single(completions);

        // The identity is whatever the player list calls them, which in live data is the full
        // Riot ID rather than a bare name.
        Assert.Equal("Rakip1#TR1", completions[0].Player);
        Assert.Equal("Renekton", completions[0].Champion);
    }

    [Fact]
    public void Reset_ForgetsEveryone()
    {
        var watcher = new ItemWatcher(Catalog);

        watcher.Observe([Enemy("Rakip1", TrinityForce)], 600);
        watcher.Reset();

        Assert.Empty(watcher.Observe([Enemy("Rakip1", TrinityForce)], 60));
    }

    [Fact]
    public void WithoutACatalogue_NothingIsReported()
    {
        var watcher = new ItemWatcher(new FakeCatalog([]));

        watcher.Observe([Enemy("Rakip1")], 600);

        Assert.Empty(watcher.Observe([Enemy("Rakip1", TrinityForce)], 640));
    }

    /// <summary>Your own completions are not news; only the enemy team reaches the panel.</summary>
    [Fact]
    public void OnlyEnemyCompletions_ReachTheViewModel()
    {
        var viewModel = new OverlayViewModel(itemCatalog: Catalog);

        viewModel.OnSnapshot(Snapshot(allyItems: [], enemyItems: []));
        viewModel.OnSnapshot(Snapshot(allyItems: [TrinityForce], enemyItems: [TrinityForce]));

        var line = Assert.Single(viewModel.EnemyItems);
        Assert.Equal("Lee Sin", line.Champion);
        Assert.True(viewModel.HasEnemyItems);
    }

    private static PlayerState Enemy(string name, params int[] items) => new()
    {
        SummonerName = $"{name}#TR1",
        RiotId = $"{name}#TR1",
        RiotIdGameName = name,
        ChampionName = name == "Rakip1" ? "Renekton" : "Lee Sin",
        Team = "CHAOS",
        Items = [.. items.Select(id => new PlayerItem { ItemId = id })],
    };

    private static GameSnapshot Snapshot(int[] allyItems, int[] enemyItems) => new()
    {
        ActivePlayer = new ActivePlayer { SummonerName = "Oyuncu#TR1", RiotIdGameName = "Oyuncu" },
        AllPlayers =
        [
            new PlayerState
            {
                SummonerName = "Oyuncu#TR1",
                RiotId = "Oyuncu#TR1",
                RiotIdGameName = "Oyuncu",
                ChampionName = "Orianna",
                Team = "ORDER",
                Items = [.. allyItems.Select(id => new PlayerItem { ItemId = id })],
            },
            new PlayerState
            {
                SummonerName = "Rakip2#TR1",
                RiotId = "Rakip2#TR1",
                RiotIdGameName = "Rakip2",
                ChampionName = "Lee Sin",
                Team = "CHAOS",
                Items = [.. enemyItems.Select(id => new PlayerItem { ItemId = id })],
            },
        ],
        GameData = new GameStats { GameTime = 640, GameMode = "CLASSIC", MapName = "Map11" },
    };

    private sealed class FakeCatalog(IReadOnlyList<ItemInfo> items) : IItemCatalog
    {
        public bool IsReady => items.Count > 0;

        public string Version => "16.17.1";

        public IReadOnlyList<ItemInfo> BuildTargets => [.. items.Where(i => i.IsBuildTarget)];

        public bool TryGet(int id, out ItemInfo item)
        {
            item = items.FirstOrDefault(i => i.Id == id)!;
            return item is not null;
        }
    }
}
