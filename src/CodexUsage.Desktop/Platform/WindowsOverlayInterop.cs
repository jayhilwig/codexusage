using System.Runtime.InteropServices;

namespace CodexUsage.Desktop.Platform;

internal static class WindowsOverlayInterop
{
    private const int GwlStyle = -16;
    private const int GwlExStyle = -20;
    private const uint GwHwndPrev = 3;
    private const long WsPopup = unchecked((long)0x80000000);
    private const long WsCaption = 0x00C00000L;
    private const long WsThickFrame = 0x00040000L;
    private const long WsSysMenu = 0x00080000L;
    private const long WsMinimizeBox = 0x00020000L;
    private const long WsMaximizeBox = 0x00010000L;
    private const long WsExToolWindow = 0x00000080L;
    private const long WsExNoActivate = 0x08000000L;
    private const uint SwpNoActivate = 0x0010;
    private const uint SwpNoZOrder = 0x0004;
    private const uint SwpShowWindow = 0x0040;
    private const uint SwpFrameChanged = 0x0020;
    private const uint SwpNoMove = 0x0002;
    private const uint SwpNoSize = 0x0001;

    public static void ConfigureHud(nint hudWindow)
    {
        if (!OperatingSystem.IsWindows() || hudWindow == nint.Zero)
        {
            return;
        }

        var style = GetWindowLongPtr(hudWindow, GwlStyle).ToInt64();
        style = (style & ~(WsCaption | WsThickFrame | WsSysMenu | WsMinimizeBox | WsMaximizeBox)) | WsPopup;
        SetWindowLongPtr(hudWindow, GwlStyle, new nint(style));
        var styles = GetWindowLongPtr(hudWindow, GwlExStyle).ToInt64();
        SetWindowLongPtr(hudWindow, GwlExStyle, new nint(styles | WsExToolWindow | WsExNoActivate));
        SetWindowPos(
            hudWindow,
            nint.Zero,
            0,
            0,
            0,
            0,
            SwpNoActivate | SwpNoZOrder | SwpFrameChanged | SwpNoMove | SwpNoSize);
    }

    public static void Position(nint window, nint codexWindow, int x, int y, int width, int height)
    {
        if (!OperatingSystem.IsWindows() || window == nint.Zero)
        {
            return;
        }

        var insertAfter = IsWindow(codexWindow)
            ? GetWindow(codexWindow, GwHwndPrev)
            : nint.Zero;
        var flags = SwpNoActivate | SwpShowWindow;
        if (insertAfter == window)
        {
            flags |= SwpNoZOrder;
            insertAfter = nint.Zero;
        }

        SetWindowPos(window, insertAfter, x, y, width, height, flags);
    }

    private static nint SetWindowLongPtr(nint window, int index, nint newValue)
        => nint.Size == 8
            ? SetWindowLongPtr64(window, index, newValue)
            : new nint(SetWindowLong32(window, index, newValue.ToInt32()));

    private static nint GetWindowLongPtr(nint window, int index)
        => nint.Size == 8
            ? GetWindowLongPtr64(window, index)
            : new nint(GetWindowLong32(window, index));

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
    private static extern nint SetWindowLongPtr64(nint window, int index, nint newValue);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongW", SetLastError = true)]
    private static extern int SetWindowLong32(nint window, int index, int newValue);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern nint GetWindowLongPtr64(nint window, int index);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")]
    private static extern int GetWindowLong32(nint window, int index);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(
        nint window,
        nint insertAfter,
        int x,
        int y,
        int width,
        int height,
        uint flags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindow(nint window);

    [DllImport("user32.dll")]
    private static extern nint GetWindow(nint window, uint command);

}
