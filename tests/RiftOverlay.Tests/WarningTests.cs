using RiftOverlay.Services;
using RiftOverlay.ViewModels;

namespace RiftOverlay.Tests;

/// <summary>
/// The warning panel exists to make silent failures visible, so its own behaviour has to be
/// exact: it appears only on a real problem, never repeats itself, and never clears just because
/// a game ended.
/// </summary>
public class WarningTests
{
    [Fact]
    public void WithNothingWrong_ThereIsNoWarningPanel()
    {
        var viewModel = new OverlayViewModel();

        viewModel.ReportFailedHotkeys([]);

        Assert.False(viewModel.HasWarnings);
        Assert.Empty(viewModel.Warnings);
    }

    [Fact]
    public void RefusedHotkeys_AreListedInOneLine()
    {
        var viewModel = new OverlayViewModel();

        viewModel.ReportFailedHotkeys(["Ctrl+Alt+3", "Ctrl+Alt+Shift+3"]);

        Assert.True(viewModel.HasWarnings);
        var warning = Assert.Single(viewModel.Warnings);
        Assert.Contains("Ctrl+Alt+3", warning);
        Assert.Contains("Ctrl+Alt+Shift+3", warning);
    }

    [Fact]
    public void TheSameWarningTwice_IsShownOnce()
    {
        var viewModel = new OverlayViewModel();

        viewModel.AddWarning("Item kataloğu indirilemedi.");
        viewModel.AddWarning("Item kataloğu indirilemedi.");

        Assert.Single(viewModel.Warnings);
    }

    [Fact]
    public void DifferentWarnings_Accumulate()
    {
        var viewModel = new OverlayViewModel();

        viewModel.ReportFailedHotkeys(["Ctrl+Alt+1"]);
        viewModel.AddWarning("Item kataloğu indirilemedi.");

        Assert.Equal(2, viewModel.Warnings.Count);
    }

    [Fact]
    public void BlankText_IsIgnored()
    {
        var viewModel = new OverlayViewModel();

        viewModel.AddWarning("   ");

        Assert.False(viewModel.HasWarnings);
    }

    /// <summary>
    /// A refused hotkey is still refused after the match ends — these are application problems,
    /// not match state, so the panel reset must leave them alone.
    /// </summary>
    [Fact]
    public void AMatchEnding_DoesNotClearWarnings()
    {
        var viewModel = new OverlayViewModel();

        viewModel.ReportFailedHotkeys(["Ctrl+Alt+1"]);
        viewModel.OnStateChanged(DataSourceState.Waiting);

        Assert.True(viewModel.HasWarnings);
        Assert.Single(viewModel.Warnings);
    }
}
