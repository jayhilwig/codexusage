using System.Runtime.InteropServices;
using CodexUsage.Core.Window;

namespace CodexUsage.Desktop.Platform;

/// <summary>
/// Finds the frontmost Codex window using public CoreGraphics window metadata.
/// This deliberately does not use the Accessibility or screen-capture APIs, so
/// the HUD does not need either privacy permission.
/// </summary>
internal sealed class MacCodexWindowTracker : ICodexWindowTracker
{
    private const string CodexBundleIdentifier = "com.openai.codex";
    private int _codexProcessId;
    private int _lastFrontmostProcessId;
    private bool _lastFrontmostWasCodex;

    public bool TryGetSnapshot(out CodexWindowSnapshot? snapshot)
    {
        snapshot = null;
        if (!OperatingSystem.IsMacOS())
        {
            return false;
        }

        try
        {
            var ownProcessId = Environment.ProcessId;
            var frontmostProcessId = MacApplicationInterop.GetFrontmostProcessId();
            if (frontmostProcessId <= 0)
            {
                return false;
            }

            // Clicking the HUD or one of its cards can make this helper the active
            // application. In that case Codex is still the window immediately below it.
            if (frontmostProcessId != ownProcessId && !IsCodexProcess(frontmostProcessId))
            {
                return false;
            }

            var windowList = MacCoreGraphics.CopyOnScreenWindowList();
            if (windowList == nint.Zero)
            {
                return false;
            }

            try
            {
                var count = MacCoreGraphics.GetArrayCount(windowList);
                for (nint index = 0; index < count; index++)
                {
                    var window = MacCoreGraphics.GetArrayValue(windowList, index);
                    if (window == nint.Zero
                        || !MacCoreGraphics.TryReadInt(window, MacCoreGraphics.OwnerProcessIdKey, out var processId)
                        || processId == ownProcessId
                        || !IsCodexProcess(processId)
                        || !IsUsableMainWindow(window, out var bounds))
                    {
                        continue;
                    }

                    MacCoreGraphics.TryReadInt(window, MacCoreGraphics.WindowNumberKey, out var windowNumber);
                    _codexProcessId = processId;
                    snapshot = new CodexWindowSnapshot(
                        new nint(windowNumber),
                        bounds,
                        CaptionButtons: null,
                        DisplayScale: 1d,
                        IsVisible: true,
                        IsMinimized: false,
                        IsDarkMode: MacApplicationInterop.IsDarkAppearance());
                    return true;
                }
            }
            finally
            {
                MacCoreGraphics.Release(windowList);
            }
        }
        catch (Exception error) when (error is DllNotFoundException
                                      or EntryPointNotFoundException
                                      or BadImageFormatException
                                      or ExternalException)
        {
            System.Diagnostics.Debug.WriteLine($"macOS window discovery failed: {error}");
        }

        return false;
    }

    private bool IsCodexProcess(int processId)
    {
        if (processId == _codexProcessId)
        {
            return true;
        }

        if (processId == _lastFrontmostProcessId)
        {
            return _lastFrontmostWasCodex;
        }

        var isCodex = string.Equals(
            MacApplicationInterop.GetBundleIdentifier(processId),
            CodexBundleIdentifier,
            StringComparison.Ordinal);
        _lastFrontmostProcessId = processId;
        _lastFrontmostWasCodex = isCodex;
        if (isCodex)
        {
            _codexProcessId = processId;
        }

        return isCodex;
    }

    private static bool IsUsableMainWindow(nint window, out ScreenRect bounds)
    {
        bounds = default;
        if (!MacCoreGraphics.TryReadInt(window, MacCoreGraphics.WindowLayerKey, out var layer)
            || layer != 0
            || !MacCoreGraphics.TryReadBounds(window, out bounds)
            || bounds.Width < 320
            || bounds.Height < 180)
        {
            return false;
        }

        return !MacCoreGraphics.TryReadDouble(window, MacCoreGraphics.WindowAlphaKey, out var alpha)
            || alpha > 0.01d;
    }
}

internal static class MacCoreGraphics
{
    private const string CoreGraphicsLibrary =
        "/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics";
    private const string CoreFoundationLibrary =
        "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";
    private const uint OnScreenOnly = 1;
    private const uint ExcludeDesktopElements = 16;
    private const uint NullWindowId = 0;
    private const int SInt32NumberType = 3;
    private const int Float64NumberType = 6;
    private const uint Utf8StringEncoding = 0x08000100;

    internal static readonly nint OwnerProcessIdKey = CreateKey("kCGWindowOwnerPID");
    internal static readonly nint WindowNumberKey = CreateKey("kCGWindowNumber");
    internal static readonly nint WindowLayerKey = CreateKey("kCGWindowLayer");
    internal static readonly nint WindowAlphaKey = CreateKey("kCGWindowAlpha");
    private static readonly nint WindowBoundsKey = CreateKey("kCGWindowBounds");

    internal static nint CopyOnScreenWindowList() =>
        CGWindowListCopyWindowInfo(OnScreenOnly | ExcludeDesktopElements, NullWindowId);

    internal static nint GetArrayCount(nint array) => CFArrayGetCount(array);

    internal static nint GetArrayValue(nint array, nint index) =>
        CFArrayGetValueAtIndex(array, index);

    internal static void Release(nint value)
    {
        if (value != nint.Zero)
        {
            CFRelease(value);
        }
    }

    internal static bool TryReadInt(nint dictionary, nint key, out int value)
    {
        value = 0;
        var number = CFDictionaryGetValue(dictionary, key);
        return number != nint.Zero && CFNumberGetIntValue(number, SInt32NumberType, out value);
    }

    internal static bool TryReadDouble(nint dictionary, nint key, out double value)
    {
        value = 0;
        var number = CFDictionaryGetValue(dictionary, key);
        return number != nint.Zero && CFNumberGetDoubleValue(number, Float64NumberType, out value);
    }

    internal static bool TryReadBounds(nint dictionary, out ScreenRect bounds)
    {
        bounds = default;
        var boundsDictionary = CFDictionaryGetValue(dictionary, WindowBoundsKey);
        if (boundsDictionary == nint.Zero
            || !CGRectMakeWithDictionaryRepresentation(boundsDictionary, out var nativeBounds)
            || !double.IsFinite(nativeBounds.Origin.X)
            || !double.IsFinite(nativeBounds.Origin.Y)
            || !double.IsFinite(nativeBounds.Size.Width)
            || !double.IsFinite(nativeBounds.Size.Height))
        {
            return false;
        }

        var left = (int)Math.Round(nativeBounds.Origin.X);
        var top = (int)Math.Round(nativeBounds.Origin.Y);
        var right = (int)Math.Round(nativeBounds.Origin.X + nativeBounds.Size.Width);
        var bottom = (int)Math.Round(nativeBounds.Origin.Y + nativeBounds.Size.Height);
        if (right <= left || bottom <= top)
        {
            return false;
        }

        bounds = new ScreenRect(left, top, right, bottom);
        return true;
    }

    private static nint CreateKey(string value) =>
        CFStringCreateWithCString(nint.Zero, value, Utf8StringEncoding);

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct NativePoint
    {
        public readonly double X;
        public readonly double Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct NativeSize
    {
        public readonly double Width;
        public readonly double Height;
    }

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct NativeRect
    {
        public readonly NativePoint Origin;
        public readonly NativeSize Size;
    }

    [DllImport(CoreGraphicsLibrary)]
    private static extern nint CGWindowListCopyWindowInfo(uint option, uint relativeToWindow);

    [DllImport(CoreGraphicsLibrary)]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool CGRectMakeWithDictionaryRepresentation(
        nint dictionary,
        out NativeRect bounds);

    [DllImport(CoreFoundationLibrary)]
    private static extern nint CFArrayGetCount(nint array);

    [DllImport(CoreFoundationLibrary)]
    private static extern nint CFArrayGetValueAtIndex(nint array, nint index);

    [DllImport(CoreFoundationLibrary)]
    private static extern nint CFDictionaryGetValue(nint dictionary, nint key);

    [DllImport(CoreFoundationLibrary, EntryPoint = "CFNumberGetValue")]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool CFNumberGetIntValue(nint number, int numberType, out int value);

    [DllImport(CoreFoundationLibrary, EntryPoint = "CFNumberGetValue")]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool CFNumberGetDoubleValue(nint number, int numberType, out double value);

    [DllImport(CoreFoundationLibrary)]
    private static extern nint CFStringCreateWithCString(
        nint allocator,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string value,
        uint encoding);

    [DllImport(CoreFoundationLibrary)]
    private static extern void CFRelease(nint value);
}

internal static class MacApplicationInterop
{
    private const string ObjectiveCLibrary = "/usr/lib/libobjc.A.dylib";

    private static readonly nint SharedWorkspaceSelector = sel_registerName("sharedWorkspace");
    private static readonly nint FrontmostApplicationSelector = sel_registerName("frontmostApplication");
    private static readonly nint RunningApplicationSelector =
        sel_registerName("runningApplicationWithProcessIdentifier:");
    private static readonly nint ProcessIdentifierSelector = sel_registerName("processIdentifier");
    private static readonly nint BundleIdentifierSelector = sel_registerName("bundleIdentifier");
    private static readonly nint SharedApplicationSelector = sel_registerName("sharedApplication");
    private static readonly nint EffectiveAppearanceSelector = sel_registerName("effectiveAppearance");
    private static readonly nint NameSelector = sel_registerName("name");
    private static readonly nint Utf8StringSelector = sel_registerName("UTF8String");

    internal static int GetFrontmostProcessId()
    {
        var workspaceClass = objc_getClass("NSWorkspace");
        var workspace = SendObject(workspaceClass, SharedWorkspaceSelector);
        var application = SendObject(workspace, FrontmostApplicationSelector);
        return application == nint.Zero
            ? 0
            : SendInt(application, ProcessIdentifierSelector);
    }

    internal static string? GetBundleIdentifier(int processId)
    {
        var applicationClass = objc_getClass("NSRunningApplication");
        var application = SendObjectWithInt(applicationClass, RunningApplicationSelector, processId);
        var bundleIdentifier = application == nint.Zero
            ? nint.Zero
            : SendObject(application, BundleIdentifierSelector);
        return ReadString(bundleIdentifier);
    }

    internal static bool IsDarkAppearance()
    {
        var applicationClass = objc_getClass("NSApplication");
        var application = SendObject(applicationClass, SharedApplicationSelector);
        var appearance = application == nint.Zero
            ? nint.Zero
            : SendObject(application, EffectiveAppearanceSelector);
        var name = appearance == nint.Zero
            ? null
            : ReadString(SendObject(appearance, NameSelector));
        return name?.Contains("Dark", StringComparison.OrdinalIgnoreCase) ?? true;
    }

    private static string? ReadString(nint nativeString)
    {
        if (nativeString == nint.Zero)
        {
            return null;
        }

        var utf8 = SendObject(nativeString, Utf8StringSelector);
        return utf8 == nint.Zero ? null : Marshal.PtrToStringUTF8(utf8);
    }

    [DllImport(ObjectiveCLibrary)]
    private static extern nint objc_getClass([MarshalAs(UnmanagedType.LPUTF8Str)] string name);

    [DllImport(ObjectiveCLibrary)]
    private static extern nint sel_registerName([MarshalAs(UnmanagedType.LPUTF8Str)] string name);

    [DllImport(ObjectiveCLibrary, EntryPoint = "objc_msgSend")]
    private static extern nint SendObject(nint receiver, nint selector);

    [DllImport(ObjectiveCLibrary, EntryPoint = "objc_msgSend")]
    private static extern nint SendObjectWithInt(nint receiver, nint selector, int value);

    [DllImport(ObjectiveCLibrary, EntryPoint = "objc_msgSend")]
    private static extern int SendInt(nint receiver, nint selector);
}
