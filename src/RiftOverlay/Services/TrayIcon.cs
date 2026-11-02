using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using RiftOverlay.Interop;

namespace RiftOverlay.Services;

/// <summary>
/// The notification-area icon and its menu — the only way back to the application once the overlay
/// is hidden, and the door to the client window.
/// <para>
/// It owns its own message-only window rather than borrowing the overlay's handle. The overlay can
/// be hidden, re-created after a monitor change, or closed to the tray; hanging the icon off that
/// handle would make the icon disappear with it. A message-only window (<c>HWND_MESSAGE</c>) has no
/// pixels, never appears in Alt+Tab, and lives exactly as long as the process.
/// </para>
/// </summary>
public sealed class TrayIcon : IDisposable
{
    private static readonly IntPtr HwndMessage = new(-3);

    private readonly ILog _log;
    private readonly HwndSource _source;
    private readonly ContextMenu _menu;

    private IntPtr _icon;
    private bool _added;
    private bool _disposed;

    public TrayIcon(string tooltip, ILog? log = null)
    {
        _log = log ?? NullLog.Instance;

        _source = new HwndSource(new HwndSourceParameters
        {
            // Parenting to HWND_MESSAGE is what makes this a message-only window.
            ParentWindow = HwndMessage,
            WindowName = "RiftOverlay.Tray",
            Width = 0,
            Height = 0,
        });

        _source.AddHook(WndProc);

        _icon = NativeMethods.LoadIconFromFile(Environment.ProcessPath ?? string.Empty);
        _menu = new ContextMenu();

        _added = NativeMethods.TrayNotify(NativeMethods.NimAdd, _source.Handle, _icon, tooltip);

        if (!_added)
        {
            // Explorer can be restarting, or the icon may be hidden by policy. Neither is fatal —
            // the overlay and its hotkeys work regardless — but silence here would look like a
            // crash, so say it once.
            _log.Error("Tray ikonu eklenemedi; overlay ve kısayollar çalışmaya devam ediyor.");
        }
    }

    public event EventHandler? OpenClientRequested;

    public event EventHandler? ToggleOverlayRequested;

    public event EventHandler? ToggleInteractiveRequested;

    public event EventHandler? ExitRequested;

    /// <summary>
    /// Asked at the moment the menu opens, rather than kept as a flag someone has to remember to
    /// update. A cached copy would go stale the first time the overlay was toggled by its hotkey
    /// instead of by this menu, and the menu would then offer to hide an already-hidden overlay.
    /// </summary>
    public Func<bool>? IsOverlayVisible { get; set; }

    /// <summary>
    /// Rebuilt on every open rather than kept around, because one entry's wording depends on
    /// whether the overlay is currently showing.
    /// </summary>
    private void FillMenu()
    {
        _menu.Items.Clear();

        _menu.Items.Add(Item("Client'ı aç", () => OpenClientRequested?.Invoke(this, EventArgs.Empty)));
        _menu.Items.Add(Item(
            IsOverlayVisible?.Invoke() != false ? "Overlay'i gizle" : "Overlay'i göster",
            () => ToggleOverlayRequested?.Invoke(this, EventArgs.Empty)));
        _menu.Items.Add(Item("Etkileşim modu", () => ToggleInteractiveRequested?.Invoke(this, EventArgs.Empty)));
        _menu.Items.Add(new Separator());
        _menu.Items.Add(Item("Çıkış", () => ExitRequested?.Invoke(this, EventArgs.Empty)));
    }

    private static MenuItem Item(string header, Action onClick)
    {
        var item = new MenuItem { Header = header };
        item.Click += (_, _) => onClick();
        return item;
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg != NativeMethods.WmTrayCallback)
        {
            return IntPtr.Zero;
        }

        switch (lParam.ToInt32())
        {
            case NativeMethods.WmLeftButtonUp:
            case NativeMethods.WmLeftButtonDoubleClick:
                OpenClientRequested?.Invoke(this, EventArgs.Empty);
                handled = true;
                break;

            case NativeMethods.WmRightButtonUp:
                ShowMenu();
                handled = true;
                break;
        }

        return IntPtr.Zero;
    }

    /// <summary>
    /// Opens the menu at the cursor. The <see cref="NativeMethods.SetForegroundWindow"/> call is
    /// not decoration: without it the menu stays open after a click elsewhere, because Windows
    /// only dismisses a tray menu whose owner window holds the foreground.
    /// </summary>
    private void ShowMenu()
    {
        NativeMethods.SetForegroundWindow(_source.Handle);

        FillMenu();

        // MousePoint lets WPF resolve the screen position itself, which keeps the menu correct on
        // mixed-DPI setups where a raw GetCursorPos would land in the wrong device units.
        _menu.Placement = System.Windows.Controls.Primitives.PlacementMode.MousePoint;
        _menu.IsOpen = true;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        if (_added)
        {
            // Skipping this leaves a ghost icon in the tray until the user hovers over it.
            NativeMethods.TrayNotify(NativeMethods.NimDelete, _source.Handle, IntPtr.Zero, string.Empty);
            _added = false;
        }

        if (_icon != IntPtr.Zero)
        {
            NativeMethods.DestroyIcon(_icon);
            _icon = IntPtr.Zero;
        }

        _source.RemoveHook(WndProc);
        _source.Dispose();
    }
}
