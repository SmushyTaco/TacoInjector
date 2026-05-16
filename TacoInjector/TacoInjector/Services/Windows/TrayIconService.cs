using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace TacoInjector.Services.Windows;

internal sealed class TrayIconService : IDisposable
{
    public static TrayIconService Instance { get; } = new();

    private const uint TrayIconId = 1;
    private const uint TrayCallbackMessage = 0x8000 + 42; // WM_APP + 42

    private const uint WmNull = 0x0000;
    private const uint WmContextMenu = 0x007B;
    private const uint WmLButtonUp = 0x0202;
    private const uint WmLButtonDblClk = 0x0203;
    private const uint WmRButtonUp = 0x0205;
    private const uint NinSelect = 0x0400;
    private const uint NinKeySelect = 0x0401;

    private const uint MfString = 0x00000000;
    private const uint MfSeparator = 0x00000800;
    private const uint TpmRightButton = 0x0002;
    private const uint TpmReturnCmd = 0x0100;
    private const uint TpmNonotify = 0x0080;

    private const int ImageIcon = 1;
    private const uint LrLoadFromFile = 0x00000010;
    private const uint LrDefaultSize = 0x00000040;

    private const nuint ShowCommand = 1001;
    private const nuint ExitCommand = 1002;

    private static readonly string WindowClassName = $"TacoInjectorTrayWindow_{Environment.ProcessId}";
    private static readonly WindowProc WindowProcDelegate = WndProc;

    private nint _messageWindow;
    private nint _iconHandle;
    private bool _ownsIconHandle;
    private bool _iconAdded;
    private bool _disposed;
    private uint _taskbarCreatedMessage;

    private TrayIconService()
    {
        AppDomain.CurrentDomain.ProcessExit += (_, _) => Dispose();
    }

    public event EventHandler? RestoreRequested;
    public event EventHandler? HideRequested;
    public event EventHandler? ExitRequested;

    public void HideMainWindowToTray()
    {
        EnsureCreated();
        WindowChrome.Hide();
        ShowBalloonTip("TacoInjector", "TacoInjector is still running in the notification area.");
    }

    public void EnsureCreated()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_iconAdded)
            return;

        EnsureMessageWindow();
        EnsureIconHandle();

        unsafe
        {
            var data = CreateBaseNotifyIconData();
            data.uFlags = NotifyIconFlags.Message | NotifyIconFlags.Icon | NotifyIconFlags.Tip;
            data.uCallbackMessage = TrayCallbackMessage;
            data.hIcon = _iconHandle;
            CopyString("TacoInjector", data.szTip, 128);

            if (!Shell_NotifyIcon(NotifyIconMessage.Add, ref data))
                throw new InvalidOperationException($"Shell_NotifyIcon(NIM_ADD) failed: {Marshal.GetLastWin32Error()}");
        }

        // Do not call NIM_SETVERSION here. The newer callback format is different
        // and was the reason the tray icon appeared but clicks did nothing.
        _iconAdded = true;
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;

        if (_iconAdded)
        {
            var data = CreateBaseNotifyIconData();
            IgnoreBooleanResult(Shell_NotifyIcon(NotifyIconMessage.Delete, ref data));

            _iconAdded = false;
        }

        if (_messageWindow != 0)
        {
            IgnoreBooleanResult(DestroyWindow(_messageWindow));
            _messageWindow = 0;
        }

        if (_iconHandle != 0 && _ownsIconHandle)
        {
            IgnoreBooleanResult(DestroyIcon(_iconHandle));
            _iconHandle = 0;
            _ownsIconHandle = false;
        }
    }

    private void OnRestoreRequested()
    {
        RestoreRequested?.Invoke(this, EventArgs.Empty);
    }

    private void OnHideRequested()
    {
        HideRequested?.Invoke(this, EventArgs.Empty);
    }

    private void OnExitRequested()
    {
        ExitRequested?.Invoke(this, EventArgs.Empty);
    }

    private void ShowBalloonTip(string title, string message)
    {
        if (!_iconAdded)
            return;

        unsafe
        {
            var data = CreateBaseNotifyIconData();
            data.uFlags = NotifyIconFlags.Info;
            data.dwInfoFlags = NotifyIconInfoFlags.Info;
            CopyString(title, data.szInfoTitle, 64);
            CopyString(message, data.szInfo, 256);

            IgnoreBooleanResult(Shell_NotifyIcon(NotifyIconMessage.Modify, ref data));
        }
    }

    private void EnsureMessageWindow()
    {
        if (_messageWindow != 0)
            return;

        var moduleHandle = GetModuleHandle(null);
        _taskbarCreatedMessage = RegisterWindowMessage("TaskbarCreated");

        var windowClass = new WindowClassEx
        {
            cbSize = (uint)Marshal.SizeOf<WindowClassEx>(),
            hInstance = moduleHandle,
            lpfnWndProc = Marshal.GetFunctionPointerForDelegate(WindowProcDelegate),
            lpszClassName = WindowClassName
        };

        var atom = RegisterClassEx(ref windowClass);

        if (atom == 0)
        {
            var error = Marshal.GetLastWin32Error();

            if (error != 1410) // ERROR_CLASS_ALREADY_EXISTS
                throw new InvalidOperationException($"RegisterClassEx failed: {error}");
        }

        // Use a normal hidden top-level window, not HWND_MESSAGE. Popup menus and
        // foreground activation behave more reliably with a real hidden window.
        _messageWindow = CreateWindowEx(
            0,
            WindowClassName,
            "TacoInjectorTrayWindow",
            0,
            0,
            0,
            0,
            0,
            0,
            0,
            moduleHandle,
            0);

        if (_messageWindow == 0)
            throw new InvalidOperationException($"CreateWindowEx failed: {Marshal.GetLastWin32Error()}");
    }

    private void EnsureIconHandle()
    {
        if (_iconHandle != 0)
            return;

        var iconPath = Path.Combine(AppContext.BaseDirectory, "Resources", "Icons", "taco.ico");

        if (File.Exists(iconPath))
        {
            _iconHandle = LoadImage(0, iconPath, ImageIcon, 0, 0, LrLoadFromFile | LrDefaultSize);
            _ownsIconHandle = _iconHandle != 0;
        }

        if (_iconHandle == 0)
        {
            _iconHandle = LoadIcon(0, new nint(32512)); // IDI_APPLICATION
            _ownsIconHandle = false;
        }
    }

    private void ShowContextMenu()
    {
        TryApplyNativeMenuTheme();

        var menu = CreatePopupMenu();

        if (menu == 0)
            return;

        try
        {
            var mainWindowIsShown = WindowChrome.IsShown;
            var showHideText = mainWindowIsShown ? "Hide TacoInjector" : "Show TacoInjector";

            AppendMenuOrThrow(menu, MfString, ShowCommand, showHideText);
            AppendMenuOrThrow(menu, MfSeparator, 0, null);
            AppendMenuOrThrow(menu, MfString, ExitCommand, "Exit");

            if (!GetCursorPos(out var point))
                return;

            IgnoreBooleanResult(SetForegroundWindow(_messageWindow));

            var command = TrackPopupMenu(
                menu,
                TpmReturnCmd | TpmRightButton | TpmNonotify,
                point.X,
                point.Y,
                0,
                _messageWindow,
                0);

            IgnoreBooleanResult(PostMessage(_messageWindow, WmNull, 0, 0));

            switch (command)
            {
                case ShowCommand:
                    if (mainWindowIsShown)
                        OnHideRequested();
                    else
                        OnRestoreRequested();
                    break;

                case ExitCommand:
                    OnExitRequested();
                    break;
            }
        }
        finally
        {
            IgnoreBooleanResult(DestroyMenu(menu));
        }
    }

    private void TryApplyNativeMenuTheme()
    {
        try
        {
            var useDarkMenu = IsSystemUsingDarkTheme();

            // Classic Win32 popup menus don't always pick up app theming from WinUI/MAUI.
            // These UxTheme calls are best-effort; if Windows changes or rejects them,
            // the tray menu simply falls back to the normal system menu.
            IgnorePreferredAppMode(SetPreferredAppMode(useDarkMenu ? PreferredAppMode.AllowDark : PreferredAppMode.Default));
            IgnoreBooleanResult(AllowDarkModeForWindow(_messageWindow, useDarkMenu));
            IgnoreHResult(SetWindowTheme(_messageWindow, useDarkMenu ? "DarkMode_Explorer" : null, null));
            FlushMenuThemes();
        }
        catch
        {
            // Native menu theming is optional. Never let it break the tray menu.
        }
    }

    private static bool IsSystemUsingDarkTheme()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            var value = key?.GetValue("AppsUseLightTheme");

            return value is 0;
        }
        catch
        {
            return false;
        }
    }

    private NotifyIconData CreateBaseNotifyIconData()
    {
        return new NotifyIconData
        {
            cbSize = (uint)Marshal.SizeOf<NotifyIconData>(),
            hWnd = _messageWindow,
            uID = TrayIconId
        };
    }

    private static unsafe void CopyString(string value, char* destination, int destinationLength)
    {
        var length = Math.Min(value.Length, destinationLength - 1);

        for (var index = 0; index < length; index++)
            destination[index] = value[index];

        destination[length] = '\0';
    }

    private static void AppendMenuOrThrow(nint menu, uint flags, nuint newItemId, string? newItem)
    {
        if (!AppendMenu(menu, flags, newItemId, newItem))
            throw new InvalidOperationException($"AppendMenu failed: {Marshal.GetLastWin32Error()}");
    }

    private static void IgnoreBooleanResult(bool value)
    {
        GC.KeepAlive(value);
    }

    private static void IgnoreHResult(int value)
    {
        GC.KeepAlive(value);
    }

    private static void IgnorePreferredAppMode(PreferredAppMode value)
    {
        GC.KeepAlive(value);
    }

    private static nint WndProc(nint hWnd, uint message, nint wParam, nint lParam)
    {
        if (message == Instance._taskbarCreatedMessage && Instance._taskbarCreatedMessage != 0)
        {
            Instance._iconAdded = false;
            Instance.EnsureCreated();
            return 0;
        }

        if (message == TrayCallbackMessage)
        {
            var rawEvent = unchecked((uint)lParam.ToInt64());
            var eventCode = rawEvent & 0xFFFF;

            switch (eventCode)
            {
                case WmLButtonUp:
                case WmLButtonDblClk:
                case NinSelect:
                case NinKeySelect:
                    Instance.OnRestoreRequested();
                    return 0;

                case WmRButtonUp:
                case WmContextMenu:
                    Instance.ShowContextMenu();
                    return 0;
            }
        }

        return DefWindowProc(hWnd, message, wParam, lParam);
    }

    private delegate nint WindowProc(nint hWnd, uint message, nint wParam, nint lParam);

    [Flags]
    private enum NotifyIconFlags : uint
    {
        Message = 0x00000001,
        Icon = 0x00000002,
        Tip = 0x00000004,
        Info = 0x00000010
    }

    private enum NotifyIconMessage : uint
    {
        Add = 0x00000000,
        Modify = 0x00000001,
        Delete = 0x00000002
    }

    private enum NotifyIconInfoFlags : uint
    {
        Info = 0x00000001
    }

    private enum PreferredAppMode
    {
        Default = 0,
        AllowDark = 1
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WindowClassEx
    {
        public uint cbSize;
        public uint style;
        public nint lpfnWndProc;
        public int cbClsExtra;
        public int cbWndExtra;
        public nint hInstance;
        public nint hIcon;
        public nint hCursor;
        public nint hbrBackground;

        [MarshalAs(UnmanagedType.LPWStr)]
        public string? lpszMenuName;

        [MarshalAs(UnmanagedType.LPWStr)]
        public string lpszClassName;

        public nint hIconSm;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private unsafe struct NotifyIconData
    {
        public uint cbSize;
        public nint hWnd;
        public uint uID;
        public NotifyIconFlags uFlags;
        public uint uCallbackMessage;
        public nint hIcon;
        public fixed char szTip[128];
        public uint dwState;
        public uint dwStateMask;
        public fixed char szInfo[256];
        public uint uVersion;
        public fixed char szInfoTitle[64];
        public NotifyIconInfoFlags dwInfoFlags;
        public Guid guidItem;
        public nint hBalloonIcon;
    }

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct Point
    {
        public readonly int X;
        public readonly int Y;
    }

    [DllImport("shell32.dll", EntryPoint = "Shell_NotifyIconW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool Shell_NotifyIcon(NotifyIconMessage message, ref NotifyIconData data);

    [DllImport("user32.dll", EntryPoint = "RegisterClassExW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern ushort RegisterClassEx(ref WindowClassEx windowClass);

    [DllImport("user32.dll", EntryPoint = "CreateWindowExW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern nint CreateWindowEx(
        uint exStyle,
        string className,
        string windowName,
        uint style,
        int x,
        int y,
        int width,
        int height,
        nint parentWindow,
        nint menu,
        nint instance,
        nint param);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyWindow(nint window);

    [DllImport("user32.dll", EntryPoint = "DefWindowProcW")]
    private static extern nint DefWindowProc(nint window, uint message, nint wParam, nint lParam);

    [DllImport("kernel32.dll", EntryPoint = "GetModuleHandleW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern nint GetModuleHandle(string? moduleName);

    [DllImport("user32.dll", EntryPoint = "LoadIconW", SetLastError = true)]
    private static extern nint LoadIcon(nint instance, nint iconName);

    [DllImport("user32.dll", EntryPoint = "LoadImageW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern nint LoadImage(nint instance, string name, int type, int cx, int cy, uint loadFlags);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(nint icon);

    [DllImport("user32.dll", EntryPoint = "RegisterWindowMessageW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint RegisterWindowMessage(string message);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PostMessage(nint window, uint message, nint wParam, nint lParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern nint CreatePopupMenu();

    [DllImport("user32.dll", EntryPoint = "AppendMenuW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AppendMenu(nint menu, uint flags, nuint newItemId, string? newItem);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyMenu(nint menu);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out Point point);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern nuint TrackPopupMenu(
        nint menu,
        uint flags,
        int x,
        int y,
        int reserved,
        nint window,
        nint rect);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(nint window);

    [DllImport("uxtheme.dll", EntryPoint = "#135")]
    private static extern PreferredAppMode SetPreferredAppMode(PreferredAppMode appMode);

    [DllImport("uxtheme.dll", EntryPoint = "#132")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AllowDarkModeForWindow(nint window, [MarshalAs(UnmanagedType.Bool)] bool allow);

    [DllImport("uxtheme.dll", EntryPoint = "#136")]
    private static extern void FlushMenuThemes();

    [DllImport("uxtheme.dll", EntryPoint = "SetWindowTheme", CharSet = CharSet.Unicode)]
    private static extern int SetWindowTheme(nint window, string? subAppName, string? subIdList);
}
