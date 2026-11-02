using System.IO;
using RiftOverlay.Services;
using RiftOverlay.ViewModels;

namespace RiftOverlay.Tests;

public class HotkeyBindingTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"riftoverlay-{Guid.NewGuid():N}.json");

    [Theory]
    [InlineData("Ctrl+Alt+O", HotkeyModifiers.Control | HotkeyModifiers.Alt, 0x4F)]
    [InlineData("Ctrl+Alt+Shift+3", HotkeyModifiers.Control | HotkeyModifiers.Alt | HotkeyModifiers.Shift, 0x33)]
    [InlineData("Alt+F4", HotkeyModifiers.Alt, 0x73)]
    [InlineData("Win+Shift+Z", HotkeyModifiers.Windows | HotkeyModifiers.Shift, 0x5A)]
    public void ACombination_ParsesToModifiersAndAKey(string text, HotkeyModifiers modifiers, int virtualKey)
    {
        var binding = HotkeyBinding.Parse(text);

        Assert.NotNull(binding);
        Assert.Equal(modifiers, binding.Modifiers);
        Assert.Equal((uint)virtualKey, binding.VirtualKey);
        Assert.False(binding.IsModifiersOnly);
    }

    /// <summary>The Flash keys are a modifier applied to 1–5, configured once rather than five times.</summary>
    [Fact]
    public void AModifierOnlyBinding_ParsesWithNoKey()
    {
        var binding = HotkeyBinding.Parse("Ctrl+Alt");

        Assert.NotNull(binding);
        Assert.True(binding.IsModifiersOnly);
        Assert.Equal(0u, binding.VirtualKey);
        Assert.Equal("Ctrl+Alt", binding.Display);
    }

    /// <summary>
    /// A binding with no modifier would swallow a bare keypress from the game — pressing "Q" would
    /// mark a Flash instead of casting.
    /// </summary>
    [Theory]
    [InlineData("O")]
    [InlineData("F5")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void SomethingWithoutAModifier_IsRejected(string? text)
    {
        Assert.Null(HotkeyBinding.Parse(text));
    }

    [Fact]
    public void AnUnmappableKey_IsRejectedRatherThanRegisteredWrong()
    {
        Assert.Null(HotkeyBinding.Parse("Ctrl+Alt+PageDown"));
        Assert.Null(HotkeyBinding.Parse("Ctrl+F13"));
    }

    [Theory]
    [InlineData("Ctrl+Alt+O")]
    [InlineData("Ctrl+Alt+Shift+5")]
    [InlineData("Alt+F1")]
    [InlineData("Ctrl+Shift")]
    public void DisplayTextRoundTrips(string text)
    {
        Assert.Equal(text, HotkeyBinding.Parse(text)!.Display);
    }

    [Fact]
    public void ModifierOrderIsNormalised()
    {
        Assert.Equal("Ctrl+Alt+Shift+A", HotkeyBinding.Parse("Shift+Alt+Ctrl+A")!.Display);
    }

    [Fact]
    public void ParsingIsCaseInsensitive()
    {
        var binding = HotkeyBinding.Parse("ctrl+alt+o");

        Assert.Equal(HotkeyModifiers.Control | HotkeyModifiers.Alt, binding!.Modifiers);
        Assert.Equal(0x4Fu, binding.VirtualKey);
    }

    /// <summary>Modifier flags are the Win32 MOD_ values so a binding registers without conversion.</summary>
    [Fact]
    public void ModifierValues_MatchWin32()
    {
        Assert.Equal(1, (int)HotkeyModifiers.Alt);
        Assert.Equal(2, (int)HotkeyModifiers.Control);
        Assert.Equal(4, (int)HotkeyModifiers.Shift);
        Assert.Equal(8, (int)HotkeyModifiers.Windows);
    }

    [Fact]
    public void EveryConfigurableBinding_HasADefaultAndALabel()
    {
        string[] ids =
        [
            HotkeyIds.Interactive, HotkeyIds.Visible, HotkeyIds.FlashModifier, HotkeyIds.SpellModifier,
        ];

        Assert.All(ids, id => Assert.True(HotkeyIds.Defaults.ContainsKey(id)));
        Assert.All(ids, id => Assert.True(HotkeyIds.Labels.ContainsKey(id)));
        Assert.All(ids, id => Assert.NotNull(HotkeyBinding.Parse(HotkeyIds.DefaultFor(id))));
    }

    [Fact]
    public void OnlyTheDigitBindings_AreModifiersOnly()
    {
        Assert.True(HotkeyIds.IsModifiersOnly(HotkeyIds.FlashModifier));
        Assert.True(HotkeyIds.IsModifiersOnly(HotkeyIds.SpellModifier));
        Assert.False(HotkeyIds.IsModifiersOnly(HotkeyIds.Interactive));
    }

    [Fact]
    public void AnUntouchedBinding_UsesItsDefault()
    {
        Assert.Equal("Ctrl+Alt+O", new OverlaySettings().HotkeyFor(HotkeyIds.Interactive));
    }

    [Fact]
    public void ARebinding_Persists()
    {
        using (var service = new SettingsService(_path))
        {
            service.Load().SetHotkey(HotkeyIds.Interactive, "Ctrl+Shift+P");
        }

        using var reopened = new SettingsService(_path);

        Assert.Equal("Ctrl+Shift+P", reopened.Load().HotkeyFor(HotkeyIds.Interactive));
    }

    [Fact]
    public void Resetting_ReturnsEveryBindingToItsDefault()
    {
        var settings = new OverlaySettings();
        settings.SetHotkey(HotkeyIds.Visible, "Ctrl+Shift+Q");

        settings.ResetHotkeys();

        Assert.Empty(settings.Hotkeys);
        Assert.Equal("Ctrl+Alt+H", settings.HotkeyFor(HotkeyIds.Visible));
    }

    /// <summary>A blank entry in a hand-edited file must not leave the overlay with no binding.</summary>
    [Fact]
    public void ABlankSavedBinding_FallsBackToTheDefault()
    {
        var settings = new OverlaySettings();
        settings.SetHotkey(HotkeyIds.Visible, "   ");

        Assert.Equal("Ctrl+Alt+H", settings.HotkeyFor(HotkeyIds.Visible));
    }

    [Fact]
    public void TheViewModel_ListsEveryBindingWithItsCurrentText()
    {
        var viewModel = new OverlayViewModel();

        viewModel.RefreshHotkeys();

        Assert.Equal(4, viewModel.Hotkeys.Count);
        Assert.Contains(viewModel.Hotkeys, h => h.Id == HotkeyIds.Interactive && h.Display == "Ctrl+Alt+O");
    }

    [Fact]
    public void OnlyOneBindingCapturesAtATime()
    {
        var viewModel = new OverlayViewModel();
        viewModel.RefreshHotkeys();

        viewModel.SetHotkeyCapturing(HotkeyIds.Visible);

        Assert.Single(viewModel.Hotkeys, h => h.IsCapturing);
        Assert.Contains("tuşa bas", viewModel.Hotkeys.Single(h => h.IsCapturing).Text);

        viewModel.SetHotkeyCapturing(null);

        Assert.DoesNotContain(viewModel.Hotkeys, h => h.IsCapturing);
    }

    public void Dispose()
    {
        if (File.Exists(_path))
        {
            File.Delete(_path);
        }
    }
}
