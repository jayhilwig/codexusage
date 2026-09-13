using CodexUsage.Core.Window;

namespace CodexUsage.Desktop.Platform;

internal static class PlatformTrackerFactory
{
    public static ICodexWindowTracker Create()
    {
        if (OperatingSystem.IsWindows())
        {
            return new WindowsCodexWindowTracker();
        }

        if (OperatingSystem.IsMacOS())
        {
            return new MacCodexWindowTracker();
        }

        return new UnsupportedCodexWindowTracker();
    }
}

internal sealed class UnsupportedCodexWindowTracker : ICodexWindowTracker
{
    public bool TryGetSnapshot(out CodexWindowSnapshot? snapshot)
    {
        snapshot = null;
        return false;
    }
}
