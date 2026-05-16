using System.Runtime.InteropServices;
using Microsoft.UI.Windowing;
using Windows.Graphics;
using WinRT.Interop;
using WinUIWindow = Microsoft.UI.Xaml.Window;

namespace TacoInjector.Services.Windows;

internal static partial class WindowChrome
{
    public const int WindowWidth = 500;
    public const int WindowHeight = 405;

    // CreateRoundRectRgn expects the corner ellipse diameter, not the MAUI radius.
    // The visible card uses a 24px radius, so this needs to be 48px to avoid
    // tiny square native-window pixels poking through at the corners.
    private const int CornerDiameter = 48;

    private const int GwlStyle = -16;
    private const long WsCaption = 0x00C00000;
    private const long WsThickFrame = 0x00040000;
    private const long WsMinimizeBox = 0x00020000;
    private const long WsMaximizeBox = 0x00010000;

    private const uint SwpNoMove = 0x0002;
    private const uint SwpNoSize = 0x0001;
    private const uint SwpNoZOrder = 0x0004;
    private const uint SwpFrameChanged = 0x0020;

    private const int SwHide = 0;
    private const int SwShow = 5;
    private const int SwMinimize = 6;
    private const int SwRestore = 9;

    private const int DwmwaBorderColor = 34;
    private const int DwmwaWindowCornerPreference = 33;
    private const uint DwmwaColorNone = 0xFFFFFFFE;
    private const int DwmwcpRound = 2;

    private static AppWindow? _appWindow;
    private static nint _hwnd;
    private static PointInt32 _dragStartPosition;

    public static nint WindowHandle => _hwnd;

    public static void Apply(WinUIWindow window)
    {
        _hwnd = WindowNative.GetWindowHandle(window);
        var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(_hwnd);
        _appWindow = AppWindow.GetFromWindowId(windowId);

        window.ExtendsContentIntoTitleBar = true;
        window.Activated += (_, _) =>
        {
            RemoveDwmBorder(_hwnd);
            ApplyRoundedWindowRegion();
        };

        if (_appWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.SetBorderAndTitleBar(hasBorder: false, hasTitleBar: false);
            presenter.IsResizable = false;
            presenter.IsMaximizable = false;
            presenter.IsMinimizable = true;
        }

        _appWindow.Resize(new SizeInt32(WindowWidth, WindowHeight));

        RemoveNativeFrame(_hwnd);
        RemoveDwmBorder(_hwnd);
        ApplyRoundedWindowRegion();
    }

    public static void BeginDrag()
    {
        if (_appWindow is null)
            return;

        _dragStartPosition = _appWindow.Position;
    }

    public static void DragBy(double totalX, double totalY)
    {
        if (_appWindow is null)
            return;

        _appWindow.Move(new PointInt32(
            _dragStartPosition.X + (int)Math.Round(totalX),
            _dragStartPosition.Y + (int)Math.Round(totalY)));
    }

    public static void Minimize()
    {
        if (_appWindow?.Presenter is OverlappedPresenter presenter)
        {
            presenter.Minimize();
            return;
        }

        if (_hwnd != 0)
            IgnorePreviousVisibility(ShowWindow(_hwnd, SwMinimize));
    }

    public static void Hide()
    {
        if (_hwnd != 0)
            IgnorePreviousVisibility(ShowWindow(_hwnd, SwHide));
    }

    public static void ShowAndActivate()
    {
        if (_hwnd == 0)
            return;

        IgnorePreviousVisibility(ShowWindow(_hwnd, SwShow));
        IgnorePreviousVisibility(ShowWindow(_hwnd, SwRestore));
        IgnoreBooleanResult(SetForegroundWindow(_hwnd));
    }

    public static bool IsShown
    {
        get
        {
            if (_hwnd == 0)
                return false;

            return IsWindowVisible(_hwnd) && !IsIconic(_hwnd);
        }
    }

    private static void RemoveNativeFrame(nint hwnd)
    {
        var style = GetWindowLongPtr(hwnd, GwlStyle).ToInt64();
        style &= ~(WsCaption | WsThickFrame | WsMinimizeBox | WsMaximizeBox);

        var previousStyle = SetWindowLongPtr(hwnd, GwlStyle, (nint)style);

        if (previousStyle == 0)
            _ = Marshal.GetLastPInvokeError();

        var frameUpdated = SetWindowPos(
            hwnd,
            hWndInsertAfter: 0,
            x: 0,
            y: 0,
            cx: 0,
            cy: 0,
            SwpNoMove | SwpNoSize | SwpNoZOrder | SwpFrameChanged);

        if (!frameUpdated)
            _ = Marshal.GetLastPInvokeError();
    }

    private static void RemoveDwmBorder(nint hwnd)
    {
        var color = DwmwaColorNone;
        IgnoreHResult(DwmSetWindowAttribute(hwnd, DwmwaBorderColor, ref color, sizeof(uint)));

        var cornerPreference = DwmwcpRound;
        IgnoreHResult(DwmSetWindowAttribute(hwnd, DwmwaWindowCornerPreference, ref cornerPreference, sizeof(int)));
    }

    private static void ApplyRoundedWindowRegion()
    {
        if (_hwnd == 0)
            return;

        var region = CreateRoundRectRgn(
            0,
            0,
            WindowWidth + 1,
            WindowHeight + 1,
            CornerDiameter,
            CornerDiameter);

        if (region == 0)
            return;

        // If SetWindowRgn succeeds, Windows owns the region handle from here.
        if (SetWindowRgn(_hwnd, region, redraw: true) == 0 && !DeleteObject(region))
            _ = Marshal.GetLastPInvokeError();
    }

    private static void IgnoreBooleanResult(bool value)
    {
        GC.KeepAlive(value);
    }

    private static void IgnorePreviousVisibility(bool value)
    {
        GC.KeepAlive(value);
    }

    private static void IgnoreHResult(int value)
    {
        GC.KeepAlive(value);
    }

    [LibraryImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
    private static partial nint GetWindowLongPtr(nint hWnd, int nIndex);

    [LibraryImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
    private static partial nint SetWindowLongPtr(nint hWnd, int nIndex, nint dwNewLong);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetWindowPos(
        nint hWnd,
        nint hWndInsertAfter,
        int x,
        int y,
        int cx,
        int cy,
        uint flags);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool ShowWindow(nint hWnd, int nCmdShow);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetForegroundWindow(nint hWnd);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool IsWindowVisible(nint hWnd);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool IsIconic(nint hWnd);

    [LibraryImport("gdi32.dll", SetLastError = true)]
    private static partial nint CreateRoundRectRgn(
        int left,
        int top,
        int right,
        int bottom,
        int widthEllipse,
        int heightEllipse);

    [LibraryImport("user32.dll", SetLastError = true)]
    private static partial int SetWindowRgn(
        nint hWnd,
        nint hRgn,
        [MarshalAs(UnmanagedType.Bool)] bool redraw);

    [LibraryImport("gdi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DeleteObject(nint hObject);

    [LibraryImport("dwmapi.dll", SetLastError = true)]
    private static partial int DwmSetWindowAttribute(
        nint hwnd,
        int dwAttribute,
        ref uint pvAttribute,
        uint cbAttribute);

    [LibraryImport("dwmapi.dll", SetLastError = true)]
    private static partial int DwmSetWindowAttribute(
        nint hwnd,
        int dwAttribute,
        ref int pvAttribute,
        uint cbAttribute);
}
