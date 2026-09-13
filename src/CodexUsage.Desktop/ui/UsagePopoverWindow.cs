using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Automation;
using L = CodexUsage.Core.Localization.Localization;
using CodexUsage.Core.Ui;

namespace CodexUsage.Desktop.Ui;

internal sealed class UsagePopoverWindow : CompanionPopoverWindow
{
    public UsagePopoverWindow() : base(248)
    {
    }

    public void Update(HudViewModel viewModel)
    {
        var now = DateTimeOffset.UtcNow;
        var content = new StackPanel { Spacing = 7 };
        var header = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        header.Children.Add(MakeTitle(L.Get("UsageRemaining")));
        var localeButton = MakeLocaleButton();
        Grid.SetColumn(localeButton, 1);
        header.Children.Add(localeButton);
        content.Children.Add(header);

        if (viewModel.Usage is { } usage
            && (usage.FiveHour is not null || usage.Weekly is not null))
        {
            content.Children.Add(MakeUsageRow(L.Get("FiveHour"), usage.FiveHour, now));
            content.Children.Add(MakeUsageRow(L.Get("Weekly"), usage.Weekly, now));
        }
        else
        {
            content.Children.Add(MakeBody(L.Get("WaitingForAccount")));
        }

        if (viewModel.Usage?.Credits is { } credits)
        {
            content.Children.Add(new Border
            {
                Height = 1,
                Margin = new Thickness(0, 3, 0, 1),
                Background = new SolidColorBrush(Color.Parse("#ECECEE")),
            });
            content.Children.Add(MakeCreditsRow(credits));
        }

        SetCard(content);
    }

    private static Button MakeLocaleButton()
    {
        var button = new Button
        {
            Content = LocaleFlag(L.Locale),
            FontSize = 13,
            Padding = new Thickness(3, 0),
            MinWidth = 22,
            Height = 20,
            Background = Brushes.Transparent,
            BorderBrush = new SolidColorBrush(Color.Parse("#59339CFF")),
            BorderThickness = new Thickness(1),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
            Cursor = new Cursor(StandardCursorType.Hand),
        };
        AutomationProperties.SetName(button, "Locale");
        var menu = new ContextMenu();
        foreach (var (locale, name) in new[]
        {
            ("en", "English"), ("de", "Deutsch"), ("ja", "日本語"), ("fr", "Français"), ("es", "Español"),
        })
        {
            var item = new MenuItem { Header = $"{LocaleFlag(locale)}  {name}" };
            item.Click += (_, _) =>
            {
                L.SetLocale(locale);
                LocalePreferences.Write(locale);
            };
            menu.Items.Add(item);
        }
        button.ContextMenu = menu;
        button.Click += (_, _) => menu.Open(button);
        return button;
    }

    private static string LocaleFlag(string locale) => locale switch
    {
        "de" => "🇩🇪",
        "ja" => "🇯🇵",
        "fr" => "🇫🇷",
        "es" => "🇪🇸",
        _ => "🇺🇸",
    };

    private static Border MakeCreditsRow(CodexUsage.Core.Usage.CreditBalance credits)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        var label = MakeSecondary(OperatingSystem.IsWindows() ? "View credits →" : L.Get("Credits"));
        label.Foreground = new SolidColorBrush(Color.Parse("#339cff"));
        var roundedBalance = Math.Round(credits.Balance, 0, MidpointRounding.AwayFromZero);
        var balance = MakeSecondary(L.Get("CreditCount", roundedBalance));
        balance.FontWeight = FontWeight.Medium;
        Grid.SetColumn(balance, 1);
        grid.Children.Add(label);
        grid.Children.Add(balance);

        var row = new Border
        {
            Padding = new Thickness(0, 2),
            Cursor = new Cursor(StandardCursorType.Hand),
            Child = grid,
        };
        if (OperatingSystem.IsWindows())
        {
            row.PointerEntered += (_, _) => label.TextDecorations = TextDecorations.Underline;
            row.PointerExited += (_, _) => label.TextDecorations = null;
        }
        row.PointerReleased += (_, eventArgs) =>
        {
            if (eventArgs.InitialPressMouseButton == MouseButton.Left && row.IsPointerOver)
            {
                OpenCreditsDashboard();
            }
        };
        return row;
    }

    private static Grid MakeUsageRow(string label, CodexUsage.Core.Usage.UsageWindow? window, DateTimeOffset now)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("54,44,*") };
        var labelText = MakeBody(label);
        var percentText = MakeBody(window is null ? "--" : $"{window.RemainingPercent}%");
        percentText.FontWeight = FontWeight.Medium;
        var resetText = MakeSecondary(HudViewModel.FormatUsageReset(window, now));
        Grid.SetColumn(labelText, 0);
        Grid.SetColumn(percentText, 1);
        Grid.SetColumn(resetText, 2);
        grid.Children.Add(labelText);
        grid.Children.Add(percentText);
        grid.Children.Add(resetText);
        return grid;
    }

    private static void OpenCreditsDashboard()
    {
        try
        {
            Process.Start(new ProcessStartInfo("https://chatgpt.com/codex/settings/usage")
            {
                UseShellExecute = true,
            });
        }
        catch
        {
        }
    }
}
