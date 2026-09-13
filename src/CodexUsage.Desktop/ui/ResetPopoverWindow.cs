using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using L = CodexUsage.Core.Localization.Localization;
using CodexUsage.Core.Ui;

namespace CodexUsage.Desktop.Ui;

internal sealed class ResetPopoverWindow : CompanionPopoverWindow
{
    public ResetPopoverWindow() : base(232)
    {
    }

    public void Update(HudViewModel viewModel)
    {
        var now = DateTimeOffset.UtcNow;
        var content = new StackPanel { Spacing = 6 };
        content.Children.Add(MakeTitle(L.Get("LatestReset")));

        if (viewModel.LatestReset is { } latest)
        {
            content.Children.Add(MakeBody(
                latest.AnnouncedAt.ToLocalTime().ToString("g", L.Culture)));
            content.Children.Add(MakeSecondary(HudViewModel.FormatLongAge(latest.AnnouncedAt, now)));
            var summary = MakeBody(
                OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()
                    ? latest.Text
                    : HudViewModel.SummarizeAnnouncement(latest.Text));
            summary.TextWrapping = TextWrapping.Wrap;
            summary.TextTrimming = TextTrimming.CharacterEllipsis;
            summary.MaxLines = 2;
            summary.FontStyle = FontStyle.Italic;
            content.Children.Add(summary);

            if (TryGetSourceUrl(latest.Source.Url, out var sourceUrl))
            {
                if (OperatingSystem.IsWindows())
                {
                    var linkText = new TextBlock
                    {
                        Text = "View source →",
                        Foreground = new SolidColorBrush(Color.Parse("#339cff")),
                        FontFamily = FontFamily.Default,
                        FontSize = 12,
                    };
                    var link = new Border
                    {
                        HorizontalAlignment = HorizontalAlignment.Left,
                        Padding = new Avalonia.Thickness(0, 2, 0, 0),
                        Background = Brushes.Transparent,
                        Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand),
                        Child = linkText,
                    };
                    link.PointerEntered += (_, _) => linkText.TextDecorations = TextDecorations.Underline;
                    link.PointerExited += (_, _) => linkText.TextDecorations = null;
                    link.PointerReleased += (_, eventArgs) =>
                    {
                        if (eventArgs.InitialPressMouseButton == Avalonia.Input.MouseButton.Left
                            && link.IsPointerOver)
                        {
                            OpenSource(sourceUrl);
                        }
                    };
                    content.Children.Add(link);
                }
                else
                {
                    var link = new Button
                    {
                        Content = new TextBlock
                        {
                            Text = $"{L.Get("ViewSource")} →",
                            Foreground = new SolidColorBrush(Color.Parse("#339cff")),
                        },
                        HorizontalAlignment = HorizontalAlignment.Left,
                        Padding = new Avalonia.Thickness(0, 2, 0, 0),
                        Background = Brushes.Transparent,
                        BorderThickness = new Avalonia.Thickness(0),
                        Foreground = new SolidColorBrush(Color.Parse("#339cff")),
                        FontFamily = FontFamily.Default,
                        FontSize = 12,
                        Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand),
                    };
                    link.Click += (_, _) => OpenSource(sourceUrl);
                    content.Children.Add(link);
                }
            }
        }
        else
        {
            content.Children.Add(MakeBody(L.Get("ResetUnavailable")));
        }

        SetCard(content);
    }

    private static bool TryGetSourceUrl(string? value, out string sourceUrl)
    {
        if (Uri.TryCreate(value, UriKind.Absolute, out var uri)
            && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp))
        {
            sourceUrl = uri.AbsoluteUri;
            return true;
        }

        sourceUrl = string.Empty;
        return false;
    }

    private static void OpenSource(string sourceUrl)
    {
        try
        {
            Process.Start(new ProcessStartInfo(sourceUrl) { UseShellExecute = true });
        }
        catch
        {
        }
    }
}
