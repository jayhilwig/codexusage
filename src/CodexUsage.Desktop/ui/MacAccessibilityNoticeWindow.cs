using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace CodexUsage.Desktop.Ui;

internal sealed class MacAccessibilityNoticeWindow : Window
{
    public MacAccessibilityNoticeWindow(Action cancel)
    {
        Width = 430;
        Height = 210;
        CanResize = false;
        ShowInTaskbar = true;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Title = "Codex Usage";
        Background = new SolidColorBrush(Color.Parse("#F7F7F7"));

        var openSettings = new Button
        {
            Content = "Open Accessibility Settings",
            HorizontalAlignment = HorizontalAlignment.Left,
            Padding = new Thickness(12, 6),
        };
        openSettings.Click += (_, _) => OpenAccessibilitySettings();

        var close = new Button
        {
            Content = "Close",
            HorizontalAlignment = HorizontalAlignment.Right,
            Padding = new Thickness(12, 6),
        };
        close.Click += (_, _) => cancel();

        Content = new Border
        {
            Background = new SolidColorBrush(Color.Parse("#F7F7F7")),
            Padding = new Thickness(22, 20),
            Child = new StackPanel
            {
                Spacing = 10,
                Children =
                {
                    new TextBlock
                    {
                        Text = "Codex Usage needs Accessibility access to follow the Codex window.",
                        FontFamily = FontFamily.Default,
                        FontSize = 14,
                        Foreground = new SolidColorBrush(Color.Parse("#1D1D1F")),
                        FontWeight = FontWeight.Medium,
                        TextWrapping = TextWrapping.Wrap,
                    },
                    new TextBlock
                    {
                        Text = "Open System Settings → Privacy & Security → Accessibility and allow Codex Usage. This window closes automatically when access is granted.",
                        FontFamily = FontFamily.Default,
                        FontSize = 13,
                        Foreground = new SolidColorBrush(Color.Parse("#3A3A3C")),
                        TextWrapping = TextWrapping.Wrap,
                    },
                    new DockPanel
                    {
                        LastChildFill = false,
                        Children = { openSettings, close },
                    },
                },
            },
        };
    }

    private static void OpenAccessibilitySettings()
    {
        if (!OperatingSystem.IsMacOS())
        {
            return;
        }

        Process.Start(new ProcessStartInfo(
            "/usr/bin/open",
            "x-apple.systempreferences:com.apple.preference.security?Privacy_Accessibility")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
        });
    }
}
