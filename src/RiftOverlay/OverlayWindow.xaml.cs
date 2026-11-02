using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using RiftOverlay.Interop;
using RiftOverlay.Services;
using RiftOverlay.ViewModels;

namespace RiftOverlay;

public partial class OverlayWindow : Window
{
    private const int HotkeyToggleInteractive = 1;
    private const int HotkeyToggleVisible = 2;

    /// <summary>Ctrl+Alt+1..5 mark an enemy's Flash; adding Shift marks their other spell.</summary>
    private const int HotkeyFlashBase = 10;
    private const int HotkeyOtherSpellBase = 20;
    private const int EnemyCount = 5;

    private const uint VkO = 0x4F;
    private const uint VkH = 0x48;
    private const uint VkOne = 0x31;

    private static readonly TimeSpan TickInterval = TimeSpan.FromMilliseconds(100);
    private static readonly TimeSpan LayoutInterval = TimeSpan.FromMilliseconds(500);

    private readonly OverlayViewModel _viewModel;
    private readonly GameWindowTracker _tracker = new();
    private readonly DispatcherTimer _timer = new();
    private readonly bool _demoMode;

    private IntPtr _handle;
    private DateTime _lastLayout = DateTime.MinValue;
    private bool _interactive;
    private bool _hiddenByUser;
    private bool _fullscreenWarned;
    private bool _hotkeysRegistered;
    private string? _capturingHotkey;
    private FrameworkElement? _dragging;
    private Point _dragOffset;

    public OverlayWindow(OverlayViewModel viewModel, bool demoMode)
    {
        InitializeComponent();

        _viewModel = viewModel;
        _demoMode = demoMode;
        DataContext = viewModel;

        _timer.Interval = TickInterval;
        _timer.Tick += OnTick;
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        _handle = new WindowInteropHelper(this).Handle;

        NativeMethods.MakeClickThrough(_handle, true);
        NativeMethods.BringToTop(_handle);

        HwndSource.FromHwnd(_handle)?.AddHook(WndProc);

        RegisterHotkeys();
        _viewModel.RefreshHotkeys();
        WireDragging();

        UpdateLayoutToGame();
        _timer.Start();
    }

    /// <summary>
    /// Windows hands a hotkey to whoever asks first, so any of these can be refused. A refused
    /// key is indistinguishable from a broken overlay unless we say so.
    /// </summary>
    private void RegisterHotkeys()
    {
        UnregisterHotkeys();

        var settings = _viewModel.Settings;
        var failed = new List<string>();

        var interactive = HotkeyBinding.Parse(settings.HotkeyFor(HotkeyIds.Interactive));
        var visible = HotkeyBinding.Parse(settings.HotkeyFor(HotkeyIds.Visible));
        var flash = HotkeyBinding.Parse(settings.HotkeyFor(HotkeyIds.FlashModifier));
        var spell = HotkeyBinding.Parse(settings.HotkeyFor(HotkeyIds.SpellModifier));

        TryRegister(HotkeyToggleInteractive, interactive, failed);
        TryRegister(HotkeyToggleVisible, visible, failed);

        // The digit bindings are the configured modifier applied to 1–5.
        for (var i = 0; i < EnemyCount; i++)
        {
            TryRegister(HotkeyFlashBase + i, WithDigit(flash, i), failed);
            TryRegister(HotkeyOtherSpellBase + i, WithDigit(spell, i), failed);
        }

        _hotkeysRegistered = true;
        _viewModel.ReportFailedHotkeys(failed);
    }

    private static HotkeyBinding? WithDigit(HotkeyBinding? modifiers, int index) =>
        modifiers is null
            ? null
            : new HotkeyBinding(modifiers.Modifiers, VkOne + (uint)index, $"{index + 1}");

    private void UnregisterHotkeys()
    {
        if (!_hotkeysRegistered)
        {
            return;
        }

        NativeMethods.UnregisterHotKey(_handle, HotkeyToggleInteractive);
        NativeMethods.UnregisterHotKey(_handle, HotkeyToggleVisible);

        for (var i = 0; i < EnemyCount; i++)
        {
            NativeMethods.UnregisterHotKey(_handle, HotkeyFlashBase + i);
            NativeMethods.UnregisterHotKey(_handle, HotkeyOtherSpellBase + i);
        }

        _hotkeysRegistered = false;
    }

    private void TryRegister(int id, HotkeyBinding? binding, List<string> failed)
    {
        if (binding is null || binding.IsModifiersOnly)
        {
            return;
        }

        if (!NativeMethods.RegisterHotKey(_handle, id, (uint)binding.Modifiers, binding.VirtualKey))
        {
            failed.Add(binding.Display);
        }
    }

    /// <summary>
    /// Records the next key combination for the binding being edited.
    /// <para>
    /// Every global hotkey is released first: while they are registered Windows delivers the
    /// combination to the hotkey handler instead of the window, so the very keys most worth
    /// rebinding would be the ones that could never be captured.
    /// </para>
    /// </summary>
    public void BeginHotkeyCapture(string id)
    {
        _capturingHotkey = id;
        UnregisterHotkeys();
        _viewModel.SetHotkeyCapturing(id);

        // Capture needs keyboard focus, which the overlay normally refuses to take.
        NativeMethods.SetActivatable(_handle, true);
        Activate();
        Focus();
    }

    private void EndHotkeyCapture()
    {
        _capturingHotkey = null;
        _viewModel.SetHotkeyCapturing(null);
        NativeMethods.SetActivatable(_handle, false);
        RegisterHotkeys();
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        base.OnPreviewKeyDown(e);

        if (_capturingHotkey is null)
        {
            return;
        }

        e.Handled = true;

        if (e.Key == Key.Escape)
        {
            EndHotkeyCapture();
            return;
        }

        var modifiers = ReadModifiers();

        if (modifiers == HotkeyModifiers.None)
        {
            return;
        }

        var id = _capturingHotkey;

        if (HotkeyIds.IsModifiersOnly(id))
        {
            _viewModel.Settings.SetHotkey(id, new HotkeyBinding(modifiers, 0, string.Empty).Display);
            EndHotkeyCapture();
            return;
        }

        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        var virtualKey = HotkeyBinding.ToVirtualKey(key.ToString());

        if (virtualKey == 0)
        {
            return;
        }

        _viewModel.Settings.SetHotkey(id, new HotkeyBinding(modifiers, virtualKey, key.ToString()).Display);
        EndHotkeyCapture();
    }

    private static HotkeyModifiers ReadModifiers()
    {
        var modifiers = HotkeyModifiers.None;

        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            modifiers |= HotkeyModifiers.Control;
        }

        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Alt))
        {
            modifiers |= HotkeyModifiers.Alt;
        }

        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
        {
            modifiers |= HotkeyModifiers.Shift;
        }

        return modifiers;
    }

    /// <summary>
    /// Panels are laid out by hand, so every one of them is a Canvas child positioned from settings
    /// rather than by alignment. Dragging only works in interaction mode, which is also the only
    /// time the overlay accepts a click at all.
    /// </summary>
    private void WireDragging()
    {
        foreach (var panel in DraggablePanels())
        {
            panel.MouseLeftButtonDown += OnPanelDragStart;
            panel.MouseMove += OnPanelDragMove;
            panel.MouseLeftButtonUp += OnPanelDragEnd;
        }
    }

    private IEnumerable<FrameworkElement> DraggablePanels() =>
        PanelHost.Children.OfType<FrameworkElement>().Where(c => c.Tag is string);

    /// <summary>
    /// Applies saved positions. Runs on every layout pass because the overlay resizes with the game
    /// window, and a fraction only becomes a pixel offset once the size is known.
    /// </summary>
    private void ApplyPanelPlacements()
    {
        if (_dragging is not null || PanelHost.ActualWidth <= 0)
        {
            return;
        }

        foreach (var panel in DraggablePanels())
        {
            var placement = _viewModel.Settings.PlacementFor((string)panel.Tag);

            var (left, top) = PanelLayout.ToPixels(
                placement,
                panel.ActualWidth,
                panel.ActualHeight,
                PanelHost.ActualWidth,
                PanelHost.ActualHeight);

            Canvas.SetLeft(panel, left);
            Canvas.SetTop(panel, top);
        }
    }

    private void OnPanelDragStart(object sender, MouseButtonEventArgs e)
    {
        if (!_interactive || sender is not FrameworkElement panel)
        {
            return;
        }

        _dragging = panel;
        _dragOffset = e.GetPosition(panel);
        panel.CaptureMouse();
        e.Handled = true;
    }

    private void OnPanelDragMove(object sender, MouseEventArgs e)
    {
        if (_dragging is null || sender != _dragging)
        {
            return;
        }

        var position = e.GetPosition(PanelHost);

        Canvas.SetLeft(_dragging, Math.Clamp(
            position.X - _dragOffset.X, 0, Math.Max(0, PanelHost.ActualWidth - _dragging.ActualWidth)));
        Canvas.SetTop(_dragging, Math.Clamp(
            position.Y - _dragOffset.Y, 0, Math.Max(0, PanelHost.ActualHeight - _dragging.ActualHeight)));
    }

    private void OnPanelDragEnd(object sender, MouseButtonEventArgs e)
    {
        if (_dragging is null)
        {
            return;
        }

        var placement = PanelLayout.ToPlacement(
            Canvas.GetLeft(_dragging),
            Canvas.GetTop(_dragging),
            PanelHost.ActualWidth,
            PanelHost.ActualHeight);

        _viewModel.Settings.SetPlacement((string)_dragging.Tag, placement.X, placement.Y);

        _dragging.ReleaseMouseCapture();
        _dragging = null;
    }

    private void OnTick(object? sender, EventArgs e)
    {
        _viewModel.Tick();

        if (DateTime.UtcNow - _lastLayout >= LayoutInterval)
        {
            _lastLayout = DateTime.UtcNow;
            UpdateLayoutToGame();
            ApplyPanelPlacements();
        }
    }

    /// <summary>
    /// Realigns the moment the scale changes instead of waiting for the next layout pass.
    /// <para>
    /// The game's rectangle arrives in physical pixels and is divided by the current scale to place
    /// the overlay, so a scale change invalidates the position immediately. Layout only runs twice a
    /// second, which left the overlay visibly offset for up to half a second after dragging the
    /// game to a monitor with different scaling — long enough to look broken. Panel placements are
    /// refreshed too, since they are fractions of a rectangle that just changed size.
    /// </para>
    /// </summary>
    protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
    {
        base.OnDpiChanged(oldDpi, newDpi);

        UpdateLayoutToGame();
        ApplyPanelPlacements();
    }

    /// <summary>
    /// Keeps the overlay glued to the client's window rectangle. Games routinely re-assert their
    /// own top-most position, so the Z-order is pushed back up on every layout pass.
    /// </summary>
    private void UpdateLayoutToGame()
    {
        if (_demoMode)
        {
            PositionAt(
                SystemParameters.WorkArea.Left,
                SystemParameters.WorkArea.Top,
                SystemParameters.WorkArea.Width,
                SystemParameters.WorkArea.Height);

            SetVisible(!_hiddenByUser);
            return;
        }

        _tracker.Refresh();

        if (!_tracker.IsRunning)
        {
            PositionAt(
                SystemParameters.WorkArea.Left,
                SystemParameters.WorkArea.Top,
                SystemParameters.WorkArea.Width,
                SystemParameters.WorkArea.Height);

            SetVisible(!_hiddenByUser);
            return;
        }

        var dpi = VisualTreeHelper.GetDpi(this);
        var bounds = _tracker.Bounds;

        PositionAt(
            bounds.Left / dpi.DpiScaleX,
            bounds.Top / dpi.DpiScaleY,
            bounds.Width / dpi.DpiScaleX,
            bounds.Height / dpi.DpiScaleY);

        CheckForExclusiveFullscreen();

        // Only draw while the client actually has focus, so the overlay never sits on top of a
        // browser or Discord while alt-tabbed out. The exception is a detected fullscreen problem:
        // the warning explaining why nothing appears in game is itself invisible in game, so it
        // has to survive until the player alt-tabs out and can read it.
        SetVisible(!_hiddenByUser && (_tracker.IsForeground || _fullscreenWarned));
    }

    /// <summary>
    /// In exclusive fullscreen Windows composites nothing over the game's swap chain, so the
    /// overlay is simply absent with no hint as to why. Warned once per session.
    /// </summary>
    private void CheckForExclusiveFullscreen()
    {
        if (_fullscreenWarned || !_tracker.IsForeground || !NativeMethods.IsExclusiveFullscreenActive())
        {
            return;
        }

        _fullscreenWarned = true;
        _viewModel.AddWarning(
            "Oyun tam ekran (exclusive fullscreen) modunda — overlay oyunun üstünde çizilemez. " +
            "Video ayarlarından Kenarlıksız (Borderless) moda geç.");
    }

    private void PositionAt(double left, double top, double width, double height)
    {
        if (width <= 0 || height <= 0)
        {
            return;
        }

        if (Math.Abs(Left - left) > 0.5 || Math.Abs(Top - top) > 0.5 ||
            Math.Abs(Width - width) > 0.5 || Math.Abs(Height - height) > 0.5)
        {
            Left = left;
            Top = top;
            Width = width;
            Height = height;
        }

        NativeMethods.BringToTop(_handle);
    }

    private void SetVisible(bool visible)
    {
        var target = visible ? Visibility.Visible : Visibility.Hidden;

        if (Visibility != target)
        {
            Visibility = target;
        }
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg != NativeMethods.WmHotkey)
        {
            return IntPtr.Zero;
        }

        var id = wParam.ToInt32();

        if (id >= HotkeyFlashBase && id < HotkeyFlashBase + EnemyCount)
        {
            _viewModel.MarkFlash(id - HotkeyFlashBase);
            handled = true;
            return IntPtr.Zero;
        }

        if (id >= HotkeyOtherSpellBase && id < HotkeyOtherSpellBase + EnemyCount)
        {
            _viewModel.MarkOtherSpell(id - HotkeyOtherSpellBase);
            handled = true;
            return IntPtr.Zero;
        }

        switch (id)
        {
            case HotkeyToggleInteractive:
                ToggleInteractive();
                handled = true;
                break;

            case HotkeyToggleVisible:
                ToggleVisible();
                handled = true;
                break;
        }

        return IntPtr.Zero;
    }

    /// <summary>
    /// Public so the tray menu drives the same code as the hotkey. Two copies of this would drift:
    /// the click-through style, the border and the view-model flag have to move together, and a
    /// second implementation that forgot one of them would leave the overlay swallowing clicks.
    /// </summary>
    public void ToggleInteractive()
    {
        _interactive = !_interactive;
        NativeMethods.MakeClickThrough(_handle, !_interactive);
        InteractiveBorder.Visibility = _interactive ? Visibility.Visible : Visibility.Collapsed;
        _viewModel.IsInteractive = _interactive;
    }

    public void ToggleVisible()
    {
        _hiddenByUser = !_hiddenByUser;
        UpdateLayoutToGame();
    }

    /// <summary>Whether the user has hidden the overlay — used for the tray menu's wording.</summary>
    public bool IsHiddenByUser => _hiddenByUser;

    private void OnHotkeyClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: HotkeyItem item })
        {
            BeginHotkeyCapture(item.Id);
        }
    }

    private void OnResetHotkeysClick(object sender, RoutedEventArgs e)
    {
        _viewModel.Settings.ResetHotkeys();
        RegisterHotkeys();
        _viewModel.RefreshHotkeys();
    }

    private void OnResetPlacementsClick(object sender, RoutedEventArgs e)
    {
        _viewModel.Settings.ResetPlacements();
        ApplyPanelPlacements();
    }

    private void OnLaneOpponentClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: LaneCandidateItem candidate })
        {
            _viewModel.SelectLaneOpponent(candidate.Player);
        }
    }

    private void OnBuildTargetClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: BuildCandidateItem candidate })
        {
            _viewModel.SelectBuildTarget(candidate.ItemId);
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        _timer.Stop();

        if (_handle != IntPtr.Zero)
        {
            UnregisterHotkeys();
        }

        base.OnClosed(e);
    }
}
