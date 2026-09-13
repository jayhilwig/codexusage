using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Themes.Fluent;
using L = CodexUsage.Core.Localization.Localization;
using CodexUsage.Core.Ui;
using CodexUsage.Desktop.Platform;
using CodexUsage.Desktop.Ui;

namespace CodexUsage.Desktop;

public sealed class App : Application
{
    private HudController? _controller;

    public override void Initialize()
    {
        Styles.Add(new FluentTheme());
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.ShutdownMode = Avalonia.Controls.ShutdownMode.OnExplicitShutdown;
            L.SetLocale(LocalePreferences.Read() ?? L.ResolveLocale(System.Globalization.CultureInfo.CurrentUICulture));
            var viewModel = new HudViewModel();
            var tracker = PlatformTrackerFactory.Create();
            var hud = new TitlebarHudWindow(viewModel, tracker, () => desktop.Shutdown());
            _controller = new HudController(viewModel, hud);
            _ = _controller.StartAsync();

            desktop.Exit += (_, _) =>
            {
                if (_controller is not null)
                {
                    _controller.DisposeAsync().AsTask().GetAwaiter().GetResult();
                }
            };
        }

        base.OnFrameworkInitializationCompleted();
    }
}
