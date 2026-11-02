using System.Diagnostics;
using RiftOverlay.Interop;

namespace RiftOverlay.Services;

/// <summary>
/// Locates the League client's render window and reports its screen rectangle so the overlay
/// can sit exactly on top of it. Only works while the game runs in borderless/windowed mode —
/// in exclusive fullscreen Windows will not composite any foreign window over the swap chain.
/// </summary>
internal sealed class GameWindowTracker
{
    private const string GameWindowClass = "RiotWindowClass";
    private const string GameProcessName = "League of Legends";

    private IntPtr _cachedHandle;

    public IntPtr Handle => _cachedHandle;

    public bool IsRunning => _cachedHandle != IntPtr.Zero;

    public bool IsForeground { get; private set; }

    public NativeMethods.Rect Bounds { get; private set; }

    /// <summary>
    /// Re-resolves the game window. Cheap enough to call a couple of times a second: the handle
    /// is cached and only re-searched once it stops being a live window.
    /// </summary>
    public void Refresh()
    {
        if (_cachedHandle != IntPtr.Zero && !NativeMethods.IsWindow(_cachedHandle))
        {
            _cachedHandle = IntPtr.Zero;
        }

        if (_cachedHandle == IntPtr.Zero)
        {
            _cachedHandle = FindGameWindow();
        }

        if (_cachedHandle == IntPtr.Zero)
        {
            IsForeground = false;
            Bounds = default;
            return;
        }

        IsForeground = NativeMethods.GetForegroundWindow() == _cachedHandle;

        if (NativeMethods.GetWindowRect(_cachedHandle, out var rect) && rect.Width > 0 && rect.Height > 0)
        {
            Bounds = rect;
        }
    }

    private static IntPtr FindGameWindow()
    {
        var handle = NativeMethods.FindWindow(GameWindowClass, null);
        if (handle != IntPtr.Zero && NativeMethods.IsWindowVisible(handle))
        {
            return handle;
        }

        // The class name has changed across client rewrites before, so fall back to the process.
        foreach (var process in Process.GetProcessesByName(GameProcessName))
        {
            using (process)
            {
                if (process.MainWindowHandle != IntPtr.Zero)
                {
                    return process.MainWindowHandle;
                }
            }
        }

        return IntPtr.Zero;
    }
}
