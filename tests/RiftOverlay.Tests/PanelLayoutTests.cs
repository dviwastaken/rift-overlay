using System.IO;
using RiftOverlay.Services;
using RiftOverlay.ViewModels;

namespace RiftOverlay.Tests;

public class PanelLayoutTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"riftoverlay-{Guid.NewGuid():N}.json");

    /// <summary>
    /// Positions are stored as fractions precisely so they survive a resolution change; a pixel
    /// offset saved at 2560 wide would put the panel in the middle of the screen at 1600.
    /// </summary>
    [Fact]
    public void APlacement_LandsProportionallyAtAnySize()
    {
        var placement = new PanelPlacement { X = 0.5, Y = 0.25 };

        var (wideLeft, wideTop) = PanelLayout.ToPixels(placement, 200, 100, 2560, 1440);
        var (narrowLeft, narrowTop) = PanelLayout.ToPixels(placement, 200, 100, 1600, 900);

        Assert.Equal(1280, wideLeft);
        Assert.Equal(360, wideTop);
        Assert.Equal(800, narrowLeft);
        Assert.Equal(225, narrowTop);
    }

    /// <summary>
    /// A panel dragged to the edge on a big screen would hang off a smaller one — and a panel that
    /// is off screen cannot be grabbed to drag back.
    /// </summary>
    [Fact]
    public void APanelNearTheEdge_IsPulledFullyBackOnScreen()
    {
        var (left, top) = PanelLayout.ToPixels(new PanelPlacement { X = 0.98, Y = 0.99 }, 300, 200, 1600, 900);

        Assert.Equal(1300, left);
        Assert.Equal(700, top);
    }

    [Fact]
    public void APanelLargerThanTheHost_IsPinnedToTheOrigin()
    {
        var (left, top) = PanelLayout.ToPixels(new PanelPlacement { X = 0.5, Y = 0.5 }, 2000, 1200, 1600, 900);

        Assert.Equal(0, left);
        Assert.Equal(0, top);
    }

    [Fact]
    public void ADroppedPositionRoundTripsBackToTheSameSpot()
    {
        var placement = PanelLayout.ToPlacement(800, 225, 1600, 900);

        Assert.Equal(0.5, placement.X, precision: 6);
        Assert.Equal(0.25, placement.Y, precision: 6);

        var (left, top) = PanelLayout.ToPixels(placement, 200, 100, 1600, 900);

        Assert.Equal(800, left);
        Assert.Equal(225, top);
    }

    [Fact]
    public void ADropBeforeTheWindowHasASize_DoesNotDivideByZero()
    {
        var placement = PanelLayout.ToPlacement(100, 100, 0, 0);

        Assert.Equal(0, placement.X);
        Assert.Equal(0, placement.Y);
    }

    [Fact]
    public void EveryPanelId_HasADefault()
    {
        string[] ids =
        [
            PanelIds.Status, PanelIds.Warnings, PanelIds.Objectives, PanelIds.Build,
            PanelIds.Lane, PanelIds.Enemies, PanelIds.EnemyItems, PanelIds.Feed,
        ];

        Assert.Equal(ids.Length, PanelIds.Defaults.Count);
        Assert.All(ids, id => Assert.True(PanelIds.Defaults.ContainsKey(id)));
    }

    /// <summary>A panel added later must land where it was designed to, not at the origin.</summary>
    [Fact]
    public void AnUnknownId_StillGetsAUsablePosition()
    {
        var placement = PanelIds.DefaultFor("henuz-olmayan-panel");

        Assert.True(placement.X > 0);
        Assert.True(placement.Y > 0);
    }

    [Fact]
    public void AnUntouchedPanel_UsesItsDefault()
    {
        var settings = new OverlaySettings();

        var placement = settings.PlacementFor(PanelIds.Feed);

        Assert.Equal(PanelIds.Defaults[PanelIds.Feed].X, placement.X);
    }

    [Fact]
    public void OnlyMovedPanels_AreWrittenToSettings()
    {
        var settings = new OverlaySettings();

        settings.SetPlacement(PanelIds.Feed, 0.4, 0.6);

        Assert.Single(settings.Panels);
        Assert.Equal(0.4, settings.PlacementFor(PanelIds.Feed).X);
        Assert.Equal(PanelIds.Defaults[PanelIds.Status].X, settings.PlacementFor(PanelIds.Status).X);
    }

    [Fact]
    public void Resetting_ReturnsEveryPanelToItsDefault()
    {
        var settings = new OverlaySettings();
        settings.SetPlacement(PanelIds.Feed, 0.4, 0.6);

        settings.ResetPlacements();

        Assert.Empty(settings.Panels);
        Assert.Equal(PanelIds.Defaults[PanelIds.Feed].X, settings.PlacementFor(PanelIds.Feed).X);
    }

    /// <summary>
    /// Moving a panel mutates a dictionary, which raises no property change of its own — without an
    /// explicit notification the new position would never reach disk.
    /// </summary>
    [Fact]
    public void MovingAPanel_Persists()
    {
        using (var service = new SettingsService(_path))
        {
            service.Load().SetPlacement(PanelIds.Enemies, 0.12, 0.34);
        }

        using var reopened = new SettingsService(_path);
        var placement = reopened.Load().PlacementFor(PanelIds.Enemies);

        Assert.Equal(0.12, placement.X, precision: 6);
        Assert.Equal(0.34, placement.Y, precision: 6);
    }

    [Fact]
    public void ResettingPositions_Persists()
    {
        using (var service = new SettingsService(_path))
        {
            var settings = service.Load();
            settings.SetPlacement(PanelIds.Enemies, 0.12, 0.34);
            settings.ResetPlacements();
        }

        using var reopened = new SettingsService(_path);

        Assert.Empty(reopened.Load().Panels);
    }

    public void Dispose()
    {
        if (File.Exists(_path))
        {
            File.Delete(_path);
        }
    }
}
