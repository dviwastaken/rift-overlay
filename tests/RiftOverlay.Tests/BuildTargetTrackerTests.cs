using RiftOverlay.Models;
using RiftOverlay.Services;

namespace RiftOverlay.Tests;

public class BuildTargetTrackerTests
{
    // A miniature catalogue shaped like the real one: two finished items that share a component,
    // which is exactly the case where inference has to either pick well or admit ambiguity.
    private const int LongSword = 1036;
    private const int Pickaxe = 3133;
    private const int Sheen = 3057;
    private const int TrinityForce = 3078;
    private const int Bloodthirster = 3072;

    private static readonly FakeCatalog Catalog = new(
    [
        new ItemInfo(LongSword, "Long Sword", 350, [], false),
        new ItemInfo(Pickaxe, "Pickaxe", 875, [], false),
        new ItemInfo(Sheen, "Sheen", 1000, [LongSword], false),
        new ItemInfo(TrinityForce, "Trinity Force", 3333, [Sheen, Pickaxe], true),
        new ItemInfo(Bloodthirster, "Bloodthirster", 3400, [LongSword, Pickaxe], true),
    ]);

    [Fact]
    public void WithNoMatchingComponents_ThereIsNoTarget()
    {
        var tracker = new BuildTargetTracker(Catalog);

        Assert.Null(tracker.Resolve(Owned(), currentGold: 500));
    }

    [Fact]
    public void OwnedComponentsCountTowardTheTotal()
    {
        var tracker = new BuildTargetTracker(Catalog);

        var target = tracker.Resolve(Owned(Sheen, Pickaxe), currentGold: 0);

        Assert.NotNull(target);
        Assert.Equal(TrinityForce, target.Item.Id);
        Assert.Equal(1875, target.Progress);
        Assert.Equal(3333 - 1875, target.Remaining);
    }

    [Fact]
    public void GoldInTheWalletIsSubtractedFromWhatIsLeft()
    {
        var tracker = new BuildTargetTracker(Catalog);

        var target = tracker.Resolve(Owned(Sheen, Pickaxe), currentGold: 400)!;

        Assert.Equal(3333 - 1875 - 400, target.Remaining);
        Assert.False(target.IsAffordable());
    }

    [Fact]
    public void EnoughGold_ReportsZeroRemaining_AndNeverGoesNegative()
    {
        var tracker = new BuildTargetTracker(Catalog);

        var target = tracker.Resolve(Owned(Sheen, Pickaxe), currentGold: 9999)!;

        Assert.Equal(0, target.Remaining);
        Assert.Equal(1, target.Completion, precision: 3);
    }

    /// <summary>
    /// A Sheen is worth its own 1000 gold, not 1000 plus the Long Sword inside it. Counting both
    /// would make every recipe look further along than it is.
    /// </summary>
    [Fact]
    public void ComponentsInsideAnOwnedComponentAreNotCountedTwice()
    {
        var tracker = new BuildTargetTracker(Catalog);

        Assert.Equal(1000, tracker.MatchedGold(Catalog.Get(TrinityForce), [Sheen]));
        Assert.Equal(1000, tracker.MatchedGold(Catalog.Get(TrinityForce), [Sheen, LongSword]));
    }

    [Fact]
    public void ASingleSharedComponent_IsReportedAsAmbiguous()
    {
        var tracker = new BuildTargetTracker(Catalog);

        // A lone Pickaxe fits Trinity Force and Bloodthirster equally well.
        var target = tracker.Resolve(Owned(Pickaxe), currentGold: 0)!;

        Assert.True(target.IsAmbiguous);
    }

    [Fact]
    public void AClearlyBetterFit_IsNotAmbiguous()
    {
        var tracker = new BuildTargetTracker(Catalog);

        var target = tracker.Resolve(Owned(Sheen, Pickaxe), currentGold: 0)!;

        Assert.False(target.IsAmbiguous);
        Assert.Equal(TrinityForce, target.Item.Id);
    }

    [Fact]
    public void AManualPick_OverridesTheInferredTarget()
    {
        var tracker = new BuildTargetTracker(Catalog) { ManualTargetId = Bloodthirster };

        var target = tracker.Resolve(Owned(Sheen, Pickaxe), currentGold: 0)!;

        Assert.Equal(Bloodthirster, target.Item.Id);
        Assert.True(target.IsManual);
        // Sheen is not part of a Bloodthirster, so only the Pickaxe counts.
        Assert.Equal(875, target.Progress);
    }

    [Fact]
    public void ClearingTheManualPick_ReturnsToInference()
    {
        var tracker = new BuildTargetTracker(Catalog) { ManualTargetId = Bloodthirster };
        tracker.ManualTargetId = null;

        Assert.Equal(TrinityForce, tracker.Resolve(Owned(Sheen, Pickaxe), 0)!.Item.Id);
    }

    [Fact]
    public void AnItemAlreadyOwned_IsNotOfferedAsATarget()
    {
        var tracker = new BuildTargetTracker(Catalog);

        var candidates = tracker.Candidates(Owned(TrinityForce, Pickaxe));

        Assert.DoesNotContain(candidates, c => c.Id == TrinityForce);
        Assert.Contains(candidates, c => c.Id == Bloodthirster);
    }

    [Fact]
    public void WithoutACatalogue_NothingIsReported()
    {
        var tracker = new BuildTargetTracker(new FakeCatalog([]));

        Assert.Null(tracker.Resolve(Owned(Sheen), 500));
        Assert.Empty(tracker.Candidates(Owned(Sheen)));
    }

    private static List<PlayerItem> Owned(params int[] ids) =>
        [.. ids.Select(id => new PlayerItem { ItemId = id })];

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

        public ItemInfo Get(int id) => items.First(i => i.Id == id);
    }
}

internal static class BuildTargetAssertions
{
    public static bool IsAffordable(this BuildTarget target) => target.Remaining == 0;
}
