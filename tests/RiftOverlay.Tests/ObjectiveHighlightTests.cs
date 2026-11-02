using System.Windows;
using RiftOverlay.Services;
using RiftOverlay.ViewModels;

namespace RiftOverlay.Tests;

/// <summary>
/// The countdown row's three states. They exist because a dragon thirty seconds out and one five
/// minutes out used to look identical, which made the panel something you had to read rather than
/// something you could glance at.
/// </summary>
public sealed class ObjectiveHighlightTests
{
    private static ObjectiveItem Item() => new(ObjectiveKind.Dragon, "Dragon");

    [Fact]
    public void ACountdownFarOut_IsNotCalledOut()
    {
        var item = Item();

        Assert.False(item.IsImminent);
        Assert.False(item.IsReady);
        Assert.Equal(FontWeights.Normal, item.RowWeight);
        Assert.Equal(Palette.For(ObjectiveKind.Dragon), item.StatusBrush);
    }

    [Fact]
    public void ACountdownAboutToExpire_TurnsAmberAndBold()
    {
        var item = Item();
        item.IsImminent = true;

        Assert.Equal(Palette.Imminent, item.StatusBrush);
        Assert.Equal(FontWeights.Bold, item.RowWeight);
    }

    [Fact]
    public void AnObjectiveInThePit_StaysDistinctFromOneAboutToSpawn()
    {
        // Both are urgent, but they call for different decisions: one is contestable now, the
        // other means "start walking". Collapsing them into one colour loses that.
        var ready = Item();
        ready.IsReady = true;
        ready.IsImminent = true;

        Assert.Equal(Palette.Ready, ready.StatusBrush);
        Assert.NotEqual(Palette.Imminent, ready.StatusBrush);
    }

    [Fact]
    public void ChangingTheStateRaisesTheBoundProperties()
    {
        // The row is bound, not redrawn: without these notifications the colour and weight would
        // only update the next time the whole list was rebuilt.
        var item = Item();
        var raised = new List<string>();

        item.PropertyChanged += (_, e) => raised.Add(e.PropertyName ?? string.Empty);

        item.IsImminent = true;

        Assert.Contains(nameof(ObjectiveItem.StatusBrush), raised);
        Assert.Contains(nameof(ObjectiveItem.RowWeight), raised);
    }

    [Theory]
    [InlineData(120, false)]
    [InlineData(31, false)]
    [InlineData(30, true)]
    [InlineData(1, true)]
    public void TheThresholdIsThirtySeconds(double remaining, bool expected) =>
        Assert.Equal(expected, remaining > 0 && remaining <= ObjectiveItem.ImminentSeconds);
}
