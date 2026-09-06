using System.Runtime.InteropServices;
using System.Text;
using CodexUsage.Core.Window;

namespace CodexUsage.Desktop.Platform;

internal sealed class WindowsCodexWindowTracker : ICodexWindowTracker
{
    private const uint ProcessQueryLimitedInformation = 0x1000;
    private const int ErrorInsufficientBuffer = 122;
    private const int DwmwaCaptionButtonBounds = 5;
    private const int DwmwaCloaked = 14;
    private const int DwmwaUseImmersiveDarkMode = 20;
    private const uint GwOwner = 4;
    private const uint GaRootOwner = 3;
    private nint _cachedWindow;

    public bool TryGetSnapshot(out CodexWindowSnapshot? snapshot)
    {
        snapshot = null;
        if (!OperatingSystem.IsWindows())
        {
            return false;
        }

        if (_cachedWindow == nint.Zero
            || !IsWindow(_cachedWindow)
            || !IsWindowVisible(_cachedWindow)
            || IsCloaked(_cachedWindow)
            || GetWindow(_cachedWindow, GwOwner) != nint.Zero
            || !IsCodexWindow(_cachedWindow))
        {
            _cachedWindow = FindCodexWindow();
        }

        if (_cachedWindow == nint.Zero || !GetWindowRect(_cachedWindow, out var bounds))
        {
            return false;
        }

        var visible = IsWindowVisible(_cachedWindow) && !IsCloaked(_cachedWindow);
        var minimized = IsIconic(_cachedWindow);
        var dpi = GetDpiForWindow(_cachedWindow);
        var scale = dpi > 0 ? dpi / 96d : 1d;
        var captionButtons = ReadCaptionButtons(_cachedWindow, bounds);
        var dark = ReadDarkMode(_cachedWindow);

        snapshot = new CodexWindowSnapshot(
            _cachedWindow,
            bounds.ToScreenRect(),
            captionButtons,
            scale,
            visible,
            minimized,
            dark);
        return true;
    }

    private static nint FindCodexWindow()
    {
        nint result = nint.Zero;
        long largestArea = -1;
        var ownProcessId = Environment.ProcessId;
        var foregroundRoot = GetAncestor(GetForegroundWindow(), GaRootOwner);
        EnumWindows((window, _) =>
        {
            if (!IsWindowVisible(window)
                || IsCloaked(window)
                || GetWindow(window, GwOwner) != nint.Zero)
            {
                return true;
            }

            GetWindowThreadProcessId(window, out var processId);
            if (processId == 0 || processId == ownProcessId)
            {
                return true;
            }

            if (IsCodexProcess(processId))
            {
                if (window == foregroundRoot)
                {
                    result = window;
                    return false;
                }

                if (GetWindowRect(window, out var bounds))
                {
                    var area = (long)Math.Max(0, bounds.Right - bounds.Left)
                        * Math.Max(0, bounds.Bottom - bounds.Top);
                    if (area > largestArea)
                    {
                        largestArea = area;
                        result = window;
                    }
                }
            }

            return true;
        }, nint.Zero);
        return result;
    }

    private static bool IsCodexWindow(nint window)
    {
        GetWindowThreadProcessId(window, out var processId);
        return processId != 0 && IsCodexProcess(processId);
    }

    private static bool IsCodexProcess(uint processId)
    {
        var process = OpenProcess(ProcessQueryLimitedInformation, false, processId);
        if (process == nint.Zero)
        {
            return false;
        }

        try
        {
            var packagedCodex = IsCodexPackage(process);
            var capacity = 2048u;
            var path = new StringBuilder((int)capacity);
            if (!QueryFullProcessImageName(process, 0, path, ref capacity))
            {
                return packagedCodex;
            }

            var executablePath = path.ToString();
            var fileName = Path.GetFileName(executablePath);
            var packagedPath = fileName.Equals("ChatGPT.exe", StringComparison.OrdinalIgnoreCase)
                && executablePath.Contains("\\WindowsApps\\OpenAI.Codex_", StringComparison.OrdinalIgnoreCase);
            var unpackagedCodex = fileName.Equals("Codex.exe", StringComparison.OrdinalIgnoreCase)
                && executablePath.Contains("\\OpenAI\\Codex", StringComparison.OrdinalIgnoreCase);
            return packagedCodex || packagedPath || unpackagedCodex;
        }
        finally
        {
            CloseHandle(process);
        }
    }

    private static bool IsCodexPackage(nint process)
    {
        uint capacity = 0;
        if (GetPackageFamilyName(process, ref capacity, null) != ErrorInsufficientBuffer
            || capacity == 0)
        {
            return false;
        }

        var familyName = new StringBuilder((int)capacity);
        return GetPackageFamilyName(process, ref capacity, familyName) == 0
            && familyName.ToString().StartsWith("OpenAI.Codex_", StringComparison.OrdinalIgnoreCase);
    }

    private static ScreenRect? ReadCaptionButtons(nint window, NativeRect windowBounds)
    {
        if (DwmGetWindowAttributeRect(
                window,
                DwmwaCaptionButtonBounds,
                out var buttons,
                Marshal.SizeOf<NativeRect>()) != 0
            || buttons.Right <= buttons.Left
            || buttons.Bottom <= buttons.Top)
        {
            return null;
        }

        // DWM documents these as window-relative. Normalize defensively in case a
        // Windows build returns screen coordinates.
        if (buttons.Left >= windowBounds.Left && buttons.Right <= windowBounds.Right)
        {
            buttons.Left -= windowBounds.Left;
            buttons.Right -= windowBounds.Left;
            buttons.Top -= windowBounds.Top;
            buttons.Bottom -= windowBounds.Top;
        }

        if (buttons.Left < 0 || buttons.Right > windowBounds.Right - windowBounds.Left)
        {
            return null;
        }

        return buttons.ToScreenRect();
    }

    private static bool IsCloaked(nint window)
    {
        return DwmGetWindowAttributeInt(window, DwmwaCloaked, out var cloaked, sizeof(int)) == 0
            && cloaked != 0;
    }

    private static bool ReadDarkMode(nint window)
    {
        return DwmGetWindowAttributeInt(
                window,
                DwmwaUseImmersiveDarkMode,
                out var dark,
                sizeof(int)) == 0
            ? dark != 0
            : true;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;

        public readonly ScreenRect ToScreenRect() => new(Left, Top, Right, Bottom);
    }

    private delegate bool EnumWindowsCallback(nint window, nint parameter);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumWindows(EnumWindowsCallback callback, nint parameter);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindow(nint window);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(nint window);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsIconic(nint window);

    [DllImport("user32.dll")]
    private static extern nint GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern nint GetAncestor(nint window, uint flags);

    [DllImport("user32.dll")]
    private static extern nint GetWindow(nint window, uint command);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(nint window, out NativeRect rect);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(nint window, out uint processId);

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(nint window);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern nint OpenProcess(uint desiredAccess, bool inheritHandle, uint processId);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool QueryFullProcessImageName(
        nint process,
        uint flags,
        StringBuilder executableName,
        ref uint size);

    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(nint handle);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetPackageFamilyName(
        nint process,
        ref uint packageFamilyNameLength,
        StringBuilder? packageFamilyName);

    [DllImport("dwmapi.dll", EntryPoint = "DwmGetWindowAttribute")]
    private static extern int DwmGetWindowAttributeRect(
        nint window,
        int attribute,
        out NativeRect value,
        int size);

    [DllImport("dwmapi.dll", EntryPoint = "DwmGetWindowAttribute")]
    private static extern int DwmGetWindowAttributeInt(
        nint window,
        int attribute,
        out int value,
        int size);
}
