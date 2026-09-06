using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Platform;
using CodexUsage.Core.Window;

namespace CodexUsage.Desktop.Platform;

// Keeps overlay behavior and native rendering out of shared usage/reset/localization code.
internal interface IPlatformHudHost
{
    void Initialize(Window window);
    void ConfigureRoot(Border root);
    void ConfigureText(TextBlock textBlock);
    void ApplyTheme(Window window, Border root, CodexWindowSnapshot? target);
    void ConfigureNativeWindow(Window window, CodexWindowSnapshot target);
    PixelPoint GetPosition(CodexWindowSnapshot target, int width, int height);
    void Position(Window window, CodexWindowSnapshot target, int x, int y, int width, int height);
}

internal static class PlatformHudHostFactory
{
    public static IPlatformHudHost Create() => OperatingSystem.IsWindows()
        ? new WindowsHudHost()
        : OperatingSystem.IsMacOS()
            ? new MacHudHost()
        : new SystemHudHost();
}

internal sealed class WindowsHudHost : IPlatformHudHost
{
    private static readonly SolidColorBrush TitlebarBrush = new(Color.Parse("#EEF4F9"));

    public void Initialize(Window window)
    {
        // Transparent Avalonia windows are layered on Windows, which prevents ClearType
        // from compositing against a known background. Keep this tiny text surface opaque.
        window.Background = TitlebarBrush;
        window.TransparencyBackgroundFallback = TitlebarBrush;
        window.TransparencyLevelHint = [WindowTransparencyLevel.None];
        window.UseLayoutRounding = true;
    }

    public void ConfigureText(TextBlock textBlock)
    {
        textBlock.FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI");
        TextOptions.SetTextRenderingMode(textBlock, TextRenderingMode.SubpixelAntialias);
        TextOptions.SetTextHintingMode(textBlock, TextHintingMode.Strong);
        TextOptions.SetBaselinePixelAlignment(textBlock, BaselinePixelAlignment.Aligned);
    }

    public void ConfigureRoot(Border root)
    {
        root.Background = TitlebarBrush;
    }

    public void ApplyTheme(Window window, Border root, CodexWindowSnapshot? target)
    {
        window.Background = TitlebarBrush;
        window.TransparencyBackgroundFallback = TitlebarBrush;
        root.Background = TitlebarBrush;
    }

    public void ConfigureNativeWindow(Window window, CodexWindowSnapshot target)
    {
        WindowsOverlayInterop.ConfigureHud(window.TryGetPlatformHandle()?.Handle ?? nint.Zero);
    }

    public PixelPoint GetPosition(CodexWindowSnapshot target, int width, int height)
    {
        var scale = Math.Max(1, target.DisplayScale);
        var margin = (int)Math.Round(8 * scale);
        int x;
        int y;
        if (target.CaptionButtons is { } buttons && buttons.Width > 0 && buttons.Height > 0)
        {
            x = target.Bounds.Left + buttons.Left - margin - width;
            y = target.Bounds.Top + buttons.Top + Math.Max(0, (buttons.Height - height) / 2);
        }
        else
        {
            var nativeButtonGroupWidth = (int)Math.Round(138 * scale);
            x = target.Bounds.Right - nativeButtonGroupWidth - margin - width;
            y = target.Bounds.Top + (int)Math.Round(5 * scale);
        }

        return new PixelPoint(Math.Max(target.Bounds.Left + margin, x), y + 3);
    }

    public void Position(Window window, CodexWindowSnapshot target, int x, int y, int width, int height)
    {
        window.Position = new PixelPoint(x, y);
        WindowsOverlayInterop.Position(
            window.TryGetPlatformHandle()?.Handle ?? nint.Zero,
            target.NativeHandle,
            x,
            y,
            width,
            height);
    }
}

internal sealed class MacHudHost : IPlatformHudHost
{
    private const double SideInset = 12;
    private const double TopInset = 5;

    public void Initialize(Window window)
    {
        window.Topmost = true;
        window.Background = Brushes.Transparent;
        window.TransparencyBackgroundFallback = Brushes.Transparent;
        window.TransparencyLevelHint = [WindowTransparencyLevel.Transparent];
        window.UseLayoutRounding = true;
    }

    public void ConfigureText(TextBlock textBlock)
    {
        // Avalonia resolves the native system UI family (San Francisco) on macOS.
        textBlock.FontFamily = FontFamily.Default;
        TextOptions.SetBaselinePixelAlignment(textBlock, BaselinePixelAlignment.Aligned);
    }

    public void ConfigureRoot(Border root)
    {
        root.Background = Brushes.Transparent;
    }

    public void ApplyTheme(Window window, Border root, CodexWindowSnapshot? target)
    {
        root.Background = Brushes.Transparent;
    }

    public void ConfigureNativeWindow(Window window, CodexWindowSnapshot target)
    {
#pragma warning disable CS0618 // Safe while the Avalonia window is open on the UI thread.
        var platformHandle = window.TryGetPlatformHandle();
        var nativeWindow = (platformHandle as IMacOSTopLevelPlatformHandle)?.NSWindow ?? nint.Zero;
#pragma warning restore CS0618
        MacOverlayInterop.ConfigureHud(nativeWindow);
    }

    public PixelPoint GetPosition(CodexWindowSnapshot target, int width, int height)
    {
        var scale = Math.Max(1, target.DisplayScale);
        var sideInset = (int)Math.Round(SideInset * scale);
        var topInset = (int)Math.Round(TopInset * scale);
        return new PixelPoint(
            Math.Max(target.Bounds.Left + sideInset, target.Bounds.Right - sideInset - width),
            target.Bounds.Top + topInset);
    }

    public void Position(Window window, CodexWindowSnapshot target, int x, int y, int width, int height) =>
        window.Position = new PixelPoint(x, y);
}

internal sealed class SystemHudHost : IPlatformHudHost
{
    public void Initialize(Window window)
    {
        window.Background = Brushes.Transparent;
        window.TransparencyBackgroundFallback = Brushes.Transparent;
        window.TransparencyLevelHint = [WindowTransparencyLevel.Transparent];
        window.UseLayoutRounding = true;
    }

    public void ConfigureText(TextBlock textBlock)
    {
        textBlock.FontFamily = FontFamily.Default;
    }

    public void ConfigureRoot(Border root)
    {
        root.Background = Brushes.Transparent;
    }

    public void ApplyTheme(Window window, Border root, CodexWindowSnapshot? target)
    {
    }

    public void ConfigureNativeWindow(Window window, CodexWindowSnapshot target)
    {
    }

    public PixelPoint GetPosition(CodexWindowSnapshot target, int width, int height)
    {
        var scale = Math.Max(1, target.DisplayScale);
        var inset = (int)Math.Round(8 * scale);
        return new PixelPoint(
            Math.Max(target.Bounds.Left + inset, target.Bounds.Right - inset - width),
            target.Bounds.Top + (int)Math.Round(5 * scale));
    }

    public void Position(Window window, CodexWindowSnapshot target, int x, int y, int width, int height) =>
        window.Position = new PixelPoint(x, y);
}
