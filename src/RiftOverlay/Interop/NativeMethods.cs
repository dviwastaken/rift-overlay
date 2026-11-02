using System.Runtime.InteropServices;

namespace RiftOverlay.Interop;

/// <summary>
/// The overlay never touches the game process — no injection, no hooking, no memory reads.
/// It is an ordinary top-most window that Windows composites above the (borderless) client,
/// so everything needed lives in user32.
/// </summary>
internal static class NativeMethods
{
    internal const int GwlExStyle = -20;

    internal const int WsExTransparent = 0x00000020;
    internal const int WsExLayered = 0x00080000;
    internal const int WsExToolWindow = 0x00000080;
    internal const int WsExNoActivate = 0x08000000;

    internal static readonly IntPtr HwndTopmost = new(-1);

    internal const uint SwpNoSize = 0x0001;
    internal const uint SwpNoMove = 0x0002;
    internal const uint SwpNoActivate = 0x0010;

    internal const int WmHotkey = 0x0312;

    internal const uint ModAlt = 0x0001;
    internal const uint ModControl = 0x0002;
    internal const uint ModShift = 0x0004;

    [StructLayout(LayoutKind.Sequential)]
    internal struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;

        public readonly int Width => Right - Left;

        public readonly int Height => Bottom - Top;
    }

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    internal static extern IntPtr FindWindow(string? lpClassName, string? lpWindowName);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetWindowRect(IntPtr hWnd, out Rect lpRect);

    [DllImport("user32.dll")]
    internal static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool IsWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
    private static extern IntPtr GetWindowLongPtr64(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongW", SetLastError = true)]
    private static extern int GetWindowLong32(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
    private static extern IntPtr SetWindowLongPtr64(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongW", SetLastError = true)]
    private static extern int SetWindowLong32(IntPtr hWnd, int nIndex, int dwNewLong);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SetWindowPos(
        IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint uFlags);

    /// <summary>
    /// Windows suppresses notifications while something holds the screen exclusively, and exposes
    /// that as a shell state. It is the only reliable way to tell exclusive fullscreen from
    /// borderless — window styles look identical in both.
    /// </summary>
    private enum UserNotificationState
    {
        NotPresent = 1,
        Busy = 2,
        RunningD3DFullScreen = 3,
        PresentationMode = 4,
        AcceptsNotifications = 5,
        QuietTime = 6,
        App = 7,
    }

    [DllImport("shell32.dll")]
    private static extern int SHQueryUserNotificationState(out UserNotificationState state);

    /// <summary>
    /// System-wide rather than per-window, so it is only meaningful while the game itself is the
    /// foreground window — otherwise another application's fullscreen would be blamed on the game.
    /// </summary>
    internal static bool IsExclusiveFullscreenActive()
    {
        try
        {
            return SHQueryUserNotificationState(out var state) == 0 &&
                   state == UserNotificationState.RunningD3DFullScreen;
        }
        catch (Exception)
        {
            return false;
        }
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    internal static IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex) =>
        IntPtr.Size == 8 ? GetWindowLongPtr64(hWnd, nIndex) : new IntPtr(GetWindowLong32(hWnd, nIndex));

    internal static void SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong)
    {
        if (IntPtr.Size == 8)
        {
            SetWindowLongPtr64(hWnd, nIndex, dwNewLong);
        }
        else
        {
            SetWindowLong32(hWnd, nIndex, dwNewLong.ToInt32());
        }
    }

    /// <summary>
    /// Layered + transparent makes hit-testing fall through to whatever is underneath, so clicks
    /// reach the game. NoActivate keeps the overlay from ever stealing focus mid-fight, and
    /// ToolWindow keeps it out of Alt+Tab.
    /// </summary>
    internal static void MakeClickThrough(IntPtr hWnd, bool clickThrough)
    {
        var style = GetWindowLongPtr(hWnd, GwlExStyle).ToInt64();
        style |= WsExLayered | WsExToolWindow | WsExNoActivate;

        if (clickThrough)
        {
            style |= WsExTransparent;
        }
        else
        {
            style &= ~(long)WsExTransparent;
        }

        SetWindowLongPtr(hWnd, GwlExStyle, new IntPtr(style));
    }

    /// <summary>
    /// The overlay refuses focus so it never steals it mid-fight, but recording a key combination
    /// needs keyboard focus. Turned on only while a binding is being captured.
    /// </summary>
    internal static void SetActivatable(IntPtr hWnd, bool activatable)
    {
        var style = GetWindowLongPtr(hWnd, GwlExStyle).ToInt64();

        if (activatable)
        {
            style &= ~(long)WsExNoActivate;
        }
        else
        {
            style |= WsExNoActivate;
        }

        SetWindowLongPtr(hWnd, GwlExStyle, new IntPtr(style));
    }

    internal static void BringToTop(IntPtr hWnd) =>
        SetWindowPos(hWnd, HwndTopmost, 0, 0, 0, 0, SwpNoMove | SwpNoSize | SwpNoActivate);

    // ---- Tray icon -------------------------------------------------------------------------
    //
    // Shell_NotifyIcon rather than a package. The project has taken no helper libraries so far —
    // not even an MVVM toolkit — and pulling in WinForms just for NotifyIcon would drag a second
    // UI framework into a WPF app to draw one 16-pixel square.

    /// <summary>Our own callback message. Anything at or above WM_APP is free for applications.</summary>
    internal const int WmTrayCallback = 0x8000 + 1;

    internal const int WmLeftButtonUp = 0x0202;
    internal const int WmRightButtonUp = 0x0205;
    internal const int WmLeftButtonDoubleClick = 0x0203;

    internal const uint NimAdd = 0x00000000;
    internal const uint NimModify = 0x00000001;
    internal const uint NimDelete = 0x00000002;

    private const uint NifMessage = 0x00000001;
    private const uint NifIcon = 0x00000002;
    private const uint NifTip = 0x00000004;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct NotifyIconData
    {
        public int CbSize;
        public IntPtr HWnd;
        public uint UId;
        public uint UFlags;
        public int UCallbackMessage;
        public IntPtr HIcon;

        /// <summary>Fixed 128 characters in the native struct; a longer tip is simply truncated.</summary>
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string SzTip;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool Shell_NotifyIconW(uint dwMessage, ref NotifyIconData lpData);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern uint ExtractIconExW(
        string lpszFile, int nIconIndex, out IntPtr phiconLarge, out IntPtr phiconSmall, uint nIcons);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool DestroyIcon(IntPtr hIcon);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SetForegroundWindow(IntPtr hWnd);

    internal static bool TrayNotify(uint message, IntPtr hWnd, IntPtr hIcon, string tip)
    {
        var data = new NotifyIconData
        {
            CbSize = Marshal.SizeOf<NotifyIconData>(),
            HWnd = hWnd,
            UId = 1,
            UFlags = NifMessage | NifIcon | NifTip,
            UCallbackMessage = WmTrayCallback,
            HIcon = hIcon,
            SzTip = tip.Length > 127 ? tip[..127] : tip,
        };

        return Shell_NotifyIconW(message, ref data);
    }

    /// <summary>
    /// Pulls the first icon out of a file — used on our own executable, so the tray icon is
    /// whatever <c>ApplicationIcon</c> compiled in and the two can never drift apart.
    /// Returns <see cref="IntPtr.Zero"/> when the file carries no icon.
    /// </summary>
    internal static IntPtr LoadIconFromFile(string path)
    {
        try
        {
            ExtractIconExW(path, 0, out var large, out var small, 1);

            // The small icon is the one drawn in the notification area; the large one would be
            // downscaled by the shell, which looks worse than the 16px frame we already ship.
            if (small != IntPtr.Zero)
            {
                if (large != IntPtr.Zero)
                {
                    DestroyIcon(large);
                }

                return small;
            }

            return large;
        }
        catch (Exception)
        {
            return IntPtr.Zero;
        }
    }
}
