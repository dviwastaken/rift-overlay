using System.IO;
using RiftOverlay.Services;

namespace RiftOverlay.Tests;

/// <summary>
/// Cleaning up cached patch data. Every test here is really asking the same question — does it
/// delete anything it should not — because this is the only code in the project that removes files
/// from disk, and getting it wrong costs more than the megabytes it saves.
/// </summary>
public sealed class CachePrunerTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(), $"riftoverlay-prune-{Guid.NewGuid():N}");

    public CachePrunerTests() => Directory.CreateDirectory(_directory);

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    private void File_(string name) => File.WriteAllText(Path.Combine(_directory, name), "{}");

    private void IconFolder(string version)
    {
        var path = Path.Combine(_directory, "icons", version);

        Directory.CreateDirectory(path);
        File.WriteAllText(Path.Combine(path, "1056.png"), "x");
    }

    private bool Exists(string relative) =>
        File.Exists(Path.Combine(_directory, relative)) || Directory.Exists(Path.Combine(_directory, relative));

    [Fact]
    public void OldCataloguesGoAndTheCurrentOneStays()
    {
        File_("items-16.16.1-tr_TR.json");
        File_("items-16.17.1-tr_TR.json");
        File_("spells-16.16.1-tr_TR.json");
        File_("spells-16.17.1-tr_TR.json");

        var removed = CachePruner.Prune(_directory, "16.17.1");

        Assert.Equal(2, removed);
        Assert.True(Exists("items-16.17.1-tr_TR.json"));
        Assert.True(Exists("spells-16.17.1-tr_TR.json"));
        Assert.False(Exists("items-16.16.1-tr_TR.json"));
        Assert.False(Exists("spells-16.16.1-tr_TR.json"));
    }

    [Fact]
    public void EveryLocaleOfTheCurrentPatchSurvives()
    {
        // The fallback locale is cached alongside the requested one; dropping it would force a
        // download the next time the preferred language is unavailable.
        File_("items-16.17.1-tr_TR.json");
        File_("items-16.17.1-en_US.json");

        CachePruner.Prune(_directory, "16.17.1");

        Assert.True(Exists("items-16.17.1-tr_TR.json"));
        Assert.True(Exists("items-16.17.1-en_US.json"));
    }

    [Fact]
    public void OldIconFoldersGo()
    {
        IconFolder("16.16.1");
        IconFolder("16.17.1");

        CachePruner.Prune(_directory, "16.17.1");

        Assert.True(Exists(Path.Combine("icons", "16.17.1")));
        Assert.False(Exists(Path.Combine("icons", "16.16.1")));
    }

    [Fact]
    public void AnythingItDoesNotRecogniseIsLeftAlone()
    {
        // The guard that matters. A stray file in the cache directory is not ours to delete, and a
        // pattern loose enough to catch it would eventually catch something that mattered.
        File_("settings.json");
        File_("notlarim.txt");
        File_("items.json");

        Directory.CreateDirectory(Path.Combine(_directory, "icons", "yedek"));

        var removed = CachePruner.Prune(_directory, "16.17.1");

        Assert.Equal(0, removed);
        Assert.True(Exists("settings.json"));
        Assert.True(Exists("notlarim.txt"));
        Assert.True(Exists("items.json"));
        Assert.True(Exists(Path.Combine("icons", "yedek")));
    }

    [Fact]
    public void WithoutAKnownPatchNothingIsTouched()
    {
        // The version is empty when the catalogue failed to load. Deleting "everything that is not
        // the current patch" at that moment would clear the entire cache, including the copy the
        // app is about to fall back on.
        File_("items-16.16.1-tr_TR.json");

        Assert.Equal(0, CachePruner.Prune(_directory, string.Empty));
        Assert.True(Exists("items-16.16.1-tr_TR.json"));
    }

    [Fact]
    public void AMissingDirectoryIsNotAnError() =>
        Assert.Equal(0, CachePruner.Prune(Path.Combine(_directory, "yok"), "16.17.1"));
}
