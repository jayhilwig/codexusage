using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Themes.Fluent;
using Avalonia.Threading;
using L = CodexUsage.Core.Localization.Localization;
using CodexUsage.Core.Ui;
using CodexUsage.Desktop.Platform;
using CodexUsage.Desktop.Ui;

namespace CodexUsage.Desktop;

public sealed class App : Application
{
    private HudController? _controller;
    private DispatcherTimer? _permissionTimer;
    private MacAccessibilityNoticeWindow? _permissionNotice;

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
            void StartHud()
            {
                if (_controller is not null)
                {
                    return;
                }

                var hud = new TitlebarHudWindow(viewModel, tracker, () => desktop.Shutdown());
                _controller = new HudController(viewModel, hud);
                _ = _controller.StartAsync();
            }

            if (tracker is IPlatformPermissionStatus permissionStatus
                && !permissionStatus.HasRequiredPermission)
            {
                var permissionGranted = false;
                _permissionNotice = new MacAccessibilityNoticeWindow(() => desktop.Shutdown());
                _permissionNotice.Closed += (_, _) =>
                {
                    if (!permissionGranted)
                    {
                        desktop.Shutdown();
                    }
                };
                _permissionNotice.Show();
                _permissionTimer = new DispatcherTimer(
                    TimeSpan.FromSeconds(1),
                    DispatcherPriority.Background,
                    (_, _) =>
                    {
                        if (!permissionStatus.HasRequiredPermission)
                        {
                            return;
                        }

                        permissionGranted = true;
                        _permissionTimer?.Stop();
                        _permissionTimer = null;
                        _permissionNotice?.Close();
                        _permissionNotice = null;
                        StartHud();
                    });
                _permissionTimer.Start();
            }
            else
            {
                StartHud();
            }

            desktop.Exit += (_, _) =>
            {
                _permissionTimer?.Stop();
                if (_controller is not null)
                {
                    _controller.DisposeAsync().AsTask().GetAwaiter().GetResult();
                }
            };
        }

        base.OnFrameworkInitializationCompleted();
    }
}
