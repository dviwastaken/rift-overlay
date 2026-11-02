using System.IO;
using RiftOverlay.Services;
using RiftOverlay.ViewModels;

namespace RiftOverlay.Tests;

public class SettingsServiceTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"riftoverlay-{Guid.NewGuid():N}.json");

    [Fact]
    public void WithNoFile_LoadsDefaults()
    {
        using var service = new SettingsService(_path);

        var settings = service.Load();

        Assert.True(settings.ShowObjectives);
        Assert.True(settings.ShowEnemies);
        Assert.Equal(1, settings.Opacity);
    }

    [Fact]
    public void ChangingAToggle_PersistsImmediately()
    {
        using (var service = new SettingsService(_path))
        {
            service.Load().ShowFeed = false;
        }

        using var reopened = new SettingsService(_path);

        Assert.False(reopened.Load().ShowFeed);
    }

    [Fact]
    public void EverySettingSurvivesARoundTrip()
    {
        using (var service = new SettingsService(_path))
        {
            var settings = service.Load();
            settings.ShowStatus = false;
            settings.ShowEnemies = false;
            settings.ShowHerald = false;
            settings.Opacity = 0.6;
        }

        using var reopened = new SettingsService(_path);
        var loaded = reopened.Load();

        Assert.False(loaded.ShowStatus);
        Assert.False(loaded.ShowEnemies);
        Assert.False(loaded.ShowHerald);
        Assert.Equal(0.6, loaded.Opacity, precision: 3);
        Assert.True(loaded.ShowObjectives);
    }

    /// <summary>A mangled file must never keep the overlay from starting.</summary>
    [Fact]
    public void CorruptFile_FallsBackToDefaultsWithoutThrowing()
    {
        File.WriteAllText(_path, "{ bu json degil ");

        using var service = new SettingsService(_path);
        var settings = service.Load();

        Assert.True(settings.ShowObjectives);
    }

    /// <summary>
    /// The Live Client API reports item names in the client's language, so the catalogue has to
    /// match the game rather than default to English.
    /// </summary>
    [Fact]
    public void ItemLocale_DefaultsToTurkish()
    {
        using var service = new SettingsService(_path);

        Assert.Equal("tr_TR", service.Load().ItemLocale);
    }

    [Fact]
    public void ABlankItemLocale_FallsBackRatherThanRequestingAnEmptyPath()
    {
        var settings = new OverlaySettings { ItemLocale = "   " };

        Assert.Equal("tr_TR", settings.ItemLocale);
    }

    [Fact]
    public void ItemLocale_SurvivesARoundTrip()
    {
        using (var service = new SettingsService(_path))
        {
            service.Load().ItemLocale = "en_US";
        }

        using var reopened = new SettingsService(_path);

        Assert.Equal("en_US", reopened.Load().ItemLocale);
    }

    [Fact]
    public void Opacity_IsClampedSoTheOverlayCannotBecomeInvisible()
    {
        var settings = new OverlaySettings { Opacity = 0 };
        Assert.Equal(0.3, settings.Opacity, precision: 3);

        settings.Opacity = 5;
        Assert.Equal(1, settings.Opacity, precision: 3);
    }

    public void Dispose()
    {
        if (File.Exists(_path))
        {
            File.Delete(_path);
        }
    }
}
