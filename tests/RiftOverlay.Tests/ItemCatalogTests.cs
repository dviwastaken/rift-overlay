using RiftOverlay.Models;
using RiftOverlay.Services;

namespace RiftOverlay.Tests;

/// <summary>
/// The Data Dragon parser had no coverage: it was verified once by hand against a real 868-item
/// file and nothing would have caught a regression. The fixture below copies the exact shapes from
/// that file, including the details that matter — ids are strings, a finished item has no "into"
/// key at all rather than an empty one, and per-map availability is a nested object.
/// </summary>
public class ItemCatalogTests
{
    private const int LongSword = 1036;
    private const int Sheen = 3057;
    private const int TrinityForce = 3078;
    private const int HealthPotion = 2003;
    private const int AramOnly = 8888;
    private const int NotForSale = 9999;

    private const string Fixture = """
    {
      "type": "item",
      "version": "16.17.1",
      "data": {
        "1036": {
          "name": "Long Sword",
          "gold": { "base": 350, "purchasable": true, "total": 350, "sell": 245 },
          "into": ["3057", "3078"],
          "maps": { "11": true, "12": true }
        },
        "3057": {
          "name": "Sheen",
          "gold": { "base": 650, "purchasable": true, "total": 900, "sell": 630 },
          "from": ["1036"],
          "into": ["3078", "3100"],
          "maps": { "11": true, "12": true }
        },
        "3078": {
          "name": "Trinity Force",
          "gold": { "base": 133, "purchasable": true, "total": 3333, "sell": 2333 },
          "from": ["3057", "1036"],
          "maps": { "11": true, "12": true }
        },
        "3072": {
          "name": "Bloodthirster",
          "gold": { "base": 850, "purchasable": true, "total": 3400, "sell": 2380 },
          "from": ["1036"],
          "maps": { "11": true, "12": true }
        },
        "2003": {
          "name": "Health Potion",
          "gold": { "base": 50, "purchasable": true, "total": 50, "sell": 20 },
          "maps": { "11": true, "12": false }
        },
        "8888": {
          "name": "Howling Abyss Only",
          "gold": { "base": 900, "purchasable": true, "total": 2500, "sell": 1750 },
          "from": ["1036"],
          "maps": { "11": false, "12": true }
        },
        "9999": {
          "name": "Not For Sale",
          "gold": { "base": 0, "purchasable": false, "total": 3000, "sell": 0 },
          "from": ["1036"],
          "maps": { "11": true }
        },
        "Aatrox": {
          "name": "Bir sayi olmayan anahtar",
          "gold": { "base": 0, "purchasable": true, "total": 100 }
        }
      }
    }
    """;

    [Fact]
    public void AnEmptyCatalogue_IsNotReady()
    {
        Assert.False(new DataDragonItemCatalog().IsReady);
    }

    [Fact]
    public void ParsingTheFixture_MakesItReady()
    {
        Assert.True(Catalog().IsReady);
    }

    [Fact]
    public void TotalGoldAndRecipe_AreReadFromTheNestedShapes()
    {
        Assert.True(Catalog().TryGet(TrinityForce, out var trinity));

        Assert.Equal("Trinity Force", trinity.Name);
        Assert.Equal(3333, trinity.TotalGold);
        Assert.Equal([Sheen, LongSword], trinity.From);
    }

    /// <summary>A finished item has no "into" key at all — not an empty array.</summary>
    [Fact]
    public void AnItemNothingBuildsOutOf_IsABuildTarget()
    {
        Assert.True(Catalog().TryGet(TrinityForce, out var trinity));

        Assert.True(trinity.IsBuildTarget);
        Assert.Contains(Catalog().BuildTargets, i => i.Id == TrinityForce);
    }

    [Fact]
    public void AComponentSomethingElseBuildsInto_IsNotABuildTarget()
    {
        var catalog = Catalog();

        Assert.True(catalog.TryGet(Sheen, out var sheen));
        Assert.False(sheen.IsBuildTarget);
        Assert.DoesNotContain(catalog.BuildTargets, i => i.Id == Sheen);
    }

    /// <summary>Consumables have no recipe, which is what separates them from finished items.</summary>
    [Fact]
    public void AnItemWithNoRecipe_IsNotABuildTarget()
    {
        var catalog = Catalog();

        Assert.True(catalog.TryGet(HealthPotion, out var potion));
        Assert.False(potion.IsBuildTarget);
    }

    [Fact]
    public void ItemsUnavailableOnTheRift_AreDroppedEntirely()
    {
        Assert.False(Catalog().TryGet(AramOnly, out _));
    }

    [Fact]
    public void UnpurchasableItems_AreDroppedEntirely()
    {
        Assert.False(Catalog().TryGet(NotForSale, out _));
    }

    /// <summary>The real file keys items by id, but the payload is not guaranteed to hold only ids.</summary>
    [Fact]
    public void NonNumericKeys_AreSkippedWithoutThrowing()
    {
        var catalog = Catalog();

        Assert.True(catalog.TryGet(LongSword, out _));
        Assert.True(catalog.TryGet(Sheen, out _));
        Assert.True(catalog.TryGet(TrinityForce, out _));
        Assert.True(catalog.TryGet(HealthPotion, out _));
        Assert.DoesNotContain(catalog.BuildTargets, i => i.Name.StartsWith("Bir sayi", StringComparison.Ordinal));
    }

    [Fact]
    public void BuildTargets_AreSortedByName()
    {
        var names = Catalog().BuildTargets.Select(i => i.Name).ToList();

        Assert.Equal(["Bloodthirster", "Trinity Force"], names);
    }

    [Fact]
    public void MalformedJson_DoesNotBringTheCatalogueUp()
    {
        var catalog = new DataDragonItemCatalog();

        Assert.ThrowsAny<Exception>(() => catalog.Parse("{ bu json degil"));
        Assert.False(catalog.IsReady);
    }

    /// <summary>
    /// End to end against parsed data rather than a hand-built catalogue: owning a Sheen should
    /// point at Trinity Force with the Sheen's full 900 gold counted once.
    /// </summary>
    [Fact]
    public void AParsedCatalogue_DrivesBuildTargetInference()
    {
        var tracker = new BuildTargetTracker(Catalog());

        var target = tracker.Resolve([new PlayerItem { ItemId = Sheen }], currentGold: 1000);

        Assert.NotNull(target);
        Assert.Equal(TrinityForce, target.Item.Id);
        Assert.Equal(900, target.Progress);
        Assert.Equal(3333 - 900 - 1000, target.Remaining);
    }

    private static DataDragonItemCatalog Catalog()
    {
        var catalog = new DataDragonItemCatalog();
        catalog.Parse(Fixture);
        return catalog;
    }
}
