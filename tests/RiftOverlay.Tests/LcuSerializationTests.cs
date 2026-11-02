using System.Text.Json;
using RiftOverlay.Services.Lcu;

namespace RiftOverlay.Tests;

/// <summary>
/// Guards the wire format of everything written to the League client.
/// <para>
/// This file exists because of a real loss. Requests were serialised with
/// <c>JsonSerializer.Serialize(body)</c> — no options — so property names went out in PascalCase.
/// The client answers 200 to such a body and keeps only the fields it recognises. Since the item-set
/// endpoint replaces the entire collection, one write produced a set with no title and no blocks
/// <em>and</em> stripped the title and blocks off a set the user already had.
/// </para>
/// <para>
/// It went unnoticed because the probe checking the round-trip built its own options with a
/// camelCase policy — it validated a serializer the write path never used. Every assertion below
/// therefore goes through <see cref="LcuJson.Options"/>, the instance the client actually writes
/// with; a test with its own options would repeat exactly the mistake it is meant to prevent.
/// </para>
/// </summary>
public sealed class LcuSerializationTests
{
    private static string Serialize(object value) => JsonSerializer.Serialize(value, LcuJson.Options);

    [Fact]
    public void PropertyNamesGoOutInCamelCase()
    {
        var json = Serialize(new ItemSet { Title = "RiftOverlay · Orianna" });

        Assert.Contains("\"title\":", json);
        Assert.DoesNotContain("\"Title\":", json);
    }

    [Fact]
    public void EveryFieldTheClientReadsIsPresentAndLowerCase()
    {
        // The full set of names captured from a live client. A property that serialises under any
        // other spelling is dropped on write, and for this endpoint dropping means deleting.
        var json = Serialize(ItemSetService.Build([1056], [3020], [6655]));

        foreach (var field in new[]
        {
            "title", "type", "map", "mode", "uid", "startedFrom", "sortrank",
            "associatedChampions", "associatedMaps", "preferredItemSlots", "blocks",
        })
        {
            Assert.Contains($"\"{field}\":", json);
        }
    }

    [Fact]
    public void BlockAndItemFieldsAreCamelCaseToo()
    {
        var json = Serialize(ItemSetService.Build([1056, 2003, 2003], [], []));

        foreach (var field in new[] { "items", "count", "id", "hideIfSummonerSpell", "showIfSummonerSpell" })
        {
            Assert.Contains($"\"{field}\":", json);
        }
    }

    [Fact]
    public void ItemIdsStaySerialisedAsStrings()
    {
        // Confirmed from a live client: "1056", not 1056.
        var json = Serialize(ItemSetService.Build([1056], [], []));

        Assert.Contains("\"id\":\"1056\"", json);
    }

    [Fact]
    public void TheCollectionWrapperKeepsTheFieldsItWasGiven()
    {
        // accountId and timestamp come from the client and are written back untouched; losing
        // either would hand the endpoint a collection it cannot place.
        var json = Serialize(new ItemSetCollection { AccountId = 209076452, Timestamp = 1234 });

        Assert.Contains("\"accountId\":209076452", json);
        Assert.Contains("\"itemSets\":", json);
        Assert.Contains("\"timestamp\":1234", json);
    }

    [Fact]
    public void ARoundTripThroughTheWriteSerialiserKeepsEverything()
    {
        // Reading was never the broken half, but the two directions have to agree: what we write
        // must read back as the same object, using the same options both ways.
        var original = ItemSetService.Build([1056, 2003, 2003], [3020], [6655, 4645]);
        original.Title = "RiftOverlay · Orianna";
        original.AssociatedChampions = [61];

        var round = JsonSerializer.Deserialize<ItemSet>(Serialize(original), LcuJson.Options);

        Assert.NotNull(round);
        Assert.Equal(original.Title, round!.Title);
        Assert.Equal(original.AssociatedChampions, round.AssociatedChampions);
        Assert.Equal(original.Blocks.Count, round.Blocks.Count);
        Assert.Equal(
            original.Blocks.Select(b => b.Type),
            round.Blocks.Select(b => b.Type));
        Assert.Equal(
            original.Blocks[0].Items.Select(i => (i.Id, i.Count)),
            round.Blocks[0].Items.Select(i => (i.Id, i.Count)));
    }

    [Fact]
    public void TheRunePageBodyIsCamelCaseAsWell()
    {
        // Rune writing goes through the same client, so it was exposed to the same bug — it simply
        // happened to use an anonymous type whose members were already lower-case.
        var json = Serialize(new RunePage
        {
            Name = "RiftOverlay · Orianna",
            PrimaryStyleId = 8200,
            SubStyleId = 8000,
            SelectedPerkIds = [8214],
        });

        Assert.Contains("\"primaryStyleId\":8200", json);
        Assert.Contains("\"selectedPerkIds\":", json);
    }
}
