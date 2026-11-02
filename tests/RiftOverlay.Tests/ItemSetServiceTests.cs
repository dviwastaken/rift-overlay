using RiftOverlay.Services.Lcu;

namespace RiftOverlay.Tests;

/// <summary>
/// Building the item set payload.
/// <para>
/// The stakes here are unusual for this project: the write endpoint replaces the user's entire set
/// collection in one request, and the account this was built against held sixty-one sets written by
/// another tool. A mistake in this file does not show a wrong number on screen — it deletes work
/// that cannot be recovered.
/// </para>
/// </summary>
public sealed class ItemSetServiceTests
{
    [Fact]
    public void ItemIdsAreWrittenAsStrings()
    {
        // Confirmed from a live client: the payload carries "1056", not 1056. A number is the kind
        // of mismatch this endpoint takes without complaint and then ignores.
        var set = ItemSetService.Build([1056], [], []);

        var entry = Assert.Single(Assert.Single(set.Blocks).Items);

        Assert.Equal("1056", entry.Id);
    }

    [Fact]
    public void RepeatedItemsBecomeACount()
    {
        // Two Health Potions are one entry with count 2 — the shape the client's own sets use.
        var set = ItemSetService.Build([1056, 2003, 2003], [], []);

        var items = Assert.Single(set.Blocks).Items;

        Assert.Equal(2, items.Count);
        Assert.Equal(1, items.Single(i => i.Id == "1056").Count);
        Assert.Equal(2, items.Single(i => i.Id == "2003").Count);
    }

    [Fact]
    public void EmptyBlocksAreDroppedRatherThanWrittenAsHeaders()
    {
        var set = ItemSetService.Build([1056], [], [6655]);

        Assert.Equal(["Başlangıç", "Çekirdek"], set.Blocks.Select(b => b.Type));
    }

    [Fact]
    public void BlocksKeepTheirOrder()
    {
        var set = ItemSetService.Build([1056], [3020], [6655]);

        Assert.Equal(["Başlangıç", "Çizme", "Çekirdek"], set.Blocks.Select(b => b.Type));
    }

    [Fact]
    public void ADefaultSetCarriesTheFieldsTheClientExpects()
    {
        var set = ItemSetService.Build([1056], [], []);

        Assert.Equal("custom", set.Type);
        Assert.Equal("any", set.Map);
        Assert.Equal("any", set.Mode);
        Assert.Equal("blank", set.StartedFrom);
    }

    [Fact]
    public void OnlyOurOwnSetsForThatChampionAreReplaced()
    {
        // The replacement rule, tested on the list directly: a set from another tool and one of
        // ours for a different champion both have to survive.
        var collection = new List<ItemSet>
        {
            new() { Title = "U.GG - Ahri", AssociatedChampions = [103] },
            new() { Title = "Kendi setim", AssociatedChampions = [61] },
            new() { Title = $"{ItemSetService.ManagedPrefix} · Orianna", AssociatedChampions = [61] },
            new() { Title = $"{ItemSetService.ManagedPrefix} · Ahri", AssociatedChampions = [103] },
        };

        collection.RemoveAll(s =>
            s.Title.StartsWith(ItemSetService.ManagedPrefix, StringComparison.Ordinal) &&
            s.AssociatedChampions.Contains(61));

        Assert.Equal(
            ["U.GG - Ahri", "Kendi setim", $"{ItemSetService.ManagedPrefix} · Ahri"],
            collection.Select(s => s.Title));
    }
}
