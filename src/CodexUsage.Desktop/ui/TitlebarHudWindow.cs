using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using CodexUsage.Core.Reset;
using L = CodexUsage.Core.Localization.Localization;
using CodexUsage.Core.Ui;
using CodexUsage.Core.Window;
using CodexUsage.Desktop.Platform;

namespace CodexUsage.Desktop.Ui;

internal sealed class TitlebarHudWindow : Window
{
    private const double HudWidth = 174;
    private const double HudHeight = 30;
    private readonly HudViewModel _viewModel;
    private readonly ICodexWindowTracker _tracker;
    private readonly IPlatformHudHost _platformHud;
    private readonly Border _surfaceRoot;
    private readonly Border _usageButton;
    private readonly Border _resetButton;
    private readonly ContextMenu _exitMenu;
    private readonly TextBlock _fiveHourText;
    private readonly TextBlock _separatorText;
    private readonly TextBlock _weeklyText;
    private readonly TextBlock _resetText;
    private readonly UsagePopoverWindow _usagePopover;
    private readonly ResetPopoverWindow _resetPopover;
    private readonly DispatcherTimer _trackerTimer;
    private readonly List<Action> _updateButtonHighlights = [];
    private CodexWindowSnapshot? _target;
    private bool _dark = true;

    public TitlebarHudWindow(
        HudViewModel viewModel,
        ICodexWindowTracker tracker,
        Action shutdown)
    {
        _viewModel = viewModel;
        _tracker = tracker;
        _platformHud = PlatformHudHostFactory.Create();
        Width = HudWidth;
        Height = HudHeight;
        MinWidth = HudWidth;
        MinHeight = HudHeight;
        MaxWidth = HudWidth;
        MaxHeight = HudHeight;
        CanResize = false;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = false;
        WindowDecorations = Avalonia.Controls.WindowDecorations.None;
        WindowStartupLocation = WindowStartupLocation.Manual;
        _platformHud.Initialize(this);

        _fiveHourText = MakeHudText("5h --");
        _separatorText = MakeHudText(" · ");
        _weeklyText = MakeHudText("W --");
        _resetText = MakeHudText("↺");
        if (OperatingSystem.IsMacOS())
        {
            _resetText.FontSize *= 1.25;
            _resetText.LineHeight = 16;
        }
        _resetText.Margin = new Thickness(0, -1, 0, 0);

        var usageLine = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 0,
            VerticalAlignment = VerticalAlignment.Center,
            Children = { _fiveHourText, _separatorText, _weeklyText },
        };
        _usageButton = MakeHudButton(usageLine, new Thickness(5, 0, 4, 0), ToggleUsagePopover);
        _resetButton = MakeHudButton(_resetText, new Thickness(4, 0, 5, 0), ToggleResetPopover);
        ToolTip.SetTip(_usageButton, L.Get("UsageRemaining"));
        ToolTip.SetTip(_resetButton, L.Get("ResetUnavailable"));

        var panel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = OperatingSystem.IsMacOS()
                ? HorizontalAlignment.Left
                : HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
            Spacing = 0,
            Children = { _usageButton, _resetButton },
        };

        var exit = new MenuItem { Header = "Exit Codex Usage" };
        _exitMenu = new ContextMenu { Items = { exit } };
        exit.Click += (_, _) =>
        {
            if (OperatingSystem.IsMacOS())
            {
                _exitMenu.Close();
            }

            shutdown();
        };
        panel.ContextMenu = _exitMenu;
        if (OperatingSystem.IsMacOS())
        {
            Deactivated += (_, _) => _exitMenu.Close();
        }
        _surfaceRoot = new Border
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            Child = panel,
        };
        if (OperatingSystem.IsMacOS())
        {
            _surfaceRoot.ContextMenu = _exitMenu;
        }
        _platformHud.ConfigureRoot(_surfaceRoot);
        Content = _surfaceRoot;

        _usagePopover = new UsagePopoverWindow();
        _resetPopover = new ResetPopoverWindow();
        _viewModel.Changed += (_, _) => Dispatcher.UIThread.Post(ApplyViewModel);
        L.Changed += (_, _) => Dispatcher.UIThread.Post(ApplyViewModel);

        Opened += (_, _) => ConfigureNativeWindow();
        var trackingInterval = OperatingSystem.IsMacOS()
            ? TimeSpan.FromMilliseconds(200)
            : TimeSpan.FromMilliseconds(100);
        _trackerTimer = new DispatcherTimer(trackingInterval, DispatcherPriority.Normal, TrackTarget);
        _trackerTimer.Start();
        ApplyViewModel();
    }

    private TextBlock MakeHudText(string text)
    {
        var textBlock = new TextBlock
        {
            Text = text,
            FontSize = 11,
            FontWeight = FontWeight.Normal,
            LineHeight = 13,
            UseLayoutRounding = true,
            VerticalAlignment = VerticalAlignment.Center,
        };
        _platformHud.ConfigureText(textBlock);
        return textBlock;
    }

    private Border MakeHudButton(Control content, Thickness padding, Action onClick)
    {
        var highlight = new Border
        {
            Padding = padding,
            Background = Brushes.Transparent,
            CornerRadius = new CornerRadius(4),
            VerticalAlignment = VerticalAlignment.Center,
            Child = content,
            MinWidth = 0,
            MinHeight = HudHeight,
            Height = HudHeight,
            Cursor = new Cursor(StandardCursorType.Hand),
            UseLayoutRounding = true,
        };

        var hovered = false;
        var pressed = false;
        void UpdateHighlight()
        {
            highlight.Background = pressed
                ? new SolidColorBrush(Color.Parse(_dark ? "#20FFFFFF" : "#17000000"))
                : hovered
                    ? new SolidColorBrush(Color.Parse(_dark ? "#14FFFFFF" : "#0D000000"))
                    : Brushes.Transparent;
        }

        highlight.PointerEntered += (_, _) =>
        {
            hovered = true;
            UpdateHighlight();
        };
        highlight.PointerExited += (_, _) =>
        {
            hovered = false;
            pressed = false;
            UpdateHighlight();
        };
        highlight.PointerPressed += (_, eventArgs) =>
        {
            pressed = eventArgs.GetCurrentPoint(highlight).Properties.IsLeftButtonPressed;
            UpdateHighlight();
        };
        highlight.PointerReleased += (_, eventArgs) =>
        {
            var activate = pressed
                && eventArgs.InitialPressMouseButton == MouseButton.Left
                && highlight.IsPointerOver;
            pressed = false;
            UpdateHighlight();
            if (activate)
            {
                onClick();
            }
        };
        highlight.PointerCaptureLost += (_, _) =>
        {
            pressed = false;
            UpdateHighlight();
        };
        _updateButtonHighlights.Add(UpdateHighlight);
        return highlight;
    }

    private void TrackTarget(object? sender, EventArgs args)
    {
        try
        {
            TrackTargetCore();
        }
        catch (Exception error)
        {
            System.Diagnostics.Debug.WriteLine($"HUD tracker failed: {error}");
            _usagePopover.Hide();
            _resetPopover.Hide();
            Hide();
        }
    }

    private void TrackTargetCore()
    {
        if (!_tracker.TryGetSnapshot(out var target)
            || target is null
            || !target.IsVisible
            || target.IsMinimized)
        {
            if (IsVisible)
            {
                _usagePopover.Hide();
                _resetPopover.Hide();
                Hide();
            }

            return;
        }

        _target = target;
        _dark = target.IsDarkMode;
        ApplyTheme();

        var scale = Math.Max(1, target.DisplayScale);
        var width = (int)Math.Round(HudWidth * scale);
        var height = (int)Math.Round(HudHeight * scale);
        var position = _platformHud.GetPosition(target, width, height);
        _platformHud.Position(this, target, position.X, position.Y, width, height);
        if (!IsVisible)
        {
            Show();
            ConfigureNativeWindow();
        }

        PositionPopovers(position.X, position.Y, width, height, scale);
    }

    private void PositionPopovers(int x, int y, int width, int height, double scale)
    {
        var gap = (int)Math.Round(6 * scale);
        if (OperatingSystem.IsMacOS())
        {
            PositionMacPopover(_usagePopover, _usageButton, alignRight: false, x, y, scale, gap);
            PositionMacPopover(_resetPopover, _resetButton, alignRight: true, x, y, scale, gap);
            return;
        }

        if (_usagePopover.IsVisible)
        {
            var popupWidth = (int)Math.Round(_usagePopover.Width * scale);
            _usagePopover.Position = new PixelPoint(x + width - popupWidth, y + height + gap);
        }

        if (_resetPopover.IsVisible)
        {
            var popupWidth = (int)Math.Round(_resetPopover.Width * scale);
            _resetPopover.Position = new PixelPoint(x + width - popupWidth, y + height + gap);
        }
    }

    private void PositionMacPopover(
        Window popup,
        Control anchor,
        bool alignRight,
        int hudX,
        int hudY,
        double scale,
        int gap)
    {
        if (!popup.IsVisible || _target is null)
        {
            return;
        }

        var anchorOrigin = anchor.TranslatePoint(default, this) ?? default;
        var anchorLeft = hudX + (int)Math.Round(anchorOrigin.X * scale);
        var anchorTop = hudY + (int)Math.Round(anchorOrigin.Y * scale);
        var anchorWidth = (int)Math.Round(anchor.Bounds.Width * scale);
        var anchorHeight = (int)Math.Round(anchor.Bounds.Height * scale);
        var popupWidth = (int)Math.Round(popup.Width * scale);
        var popupHeight = (int)Math.Ceiling(popup.Bounds.Height * scale);
        if (popupHeight <= 0)
        {
            return;
        }

        var bounds = _target.Bounds;
        var edgeMargin = (int)Math.Round(8 * scale);
        var desiredX = alignRight
            ? anchorLeft + anchorWidth - popupWidth
            : anchorLeft;
        var minX = bounds.Left + edgeMargin;
        var maxX = Math.Max(minX, bounds.Right - edgeMargin - popupWidth);
        var popupX = Math.Clamp(desiredX, minX, maxX);

        var belowY = anchorTop + anchorHeight + gap;
        var aboveY = anchorTop - gap - popupHeight;
        var desiredY = belowY + popupHeight <= bounds.Bottom - edgeMargin
            ? belowY
            : aboveY;
        var minY = bounds.Top + edgeMargin;
        var maxY = Math.Max(minY, bounds.Bottom - edgeMargin - popupHeight);
        var popupY = Math.Clamp(desiredY, minY, maxY);
        popup.Position = new PixelPoint(popupX, popupY);
    }

    private void PositionVisiblePopovers()
    {
        if (_target is null)
        {
            return;
        }

        var scale = Math.Max(1, _target.DisplayScale);
        PositionPopovers(
            Position.X,
            Position.Y,
            (int)Math.Round(HudWidth * scale),
            (int)Math.Round(HudHeight * scale),
            scale);
    }

    private void ConfigureNativeWindow()
    {
        if (_target is null)
        {
            return;
        }

        _platformHud.ConfigureNativeWindow(this, _target);
    }

    private void ToggleUsagePopover()
    {
        _resetPopover.Hide();
        if (_usagePopover.IsVisible)
        {
            _usagePopover.Hide();
            return;
        }

        _usagePopover.Update(_viewModel);
        _usagePopover.ShowFor(this);
        Dispatcher.UIThread.Post(PositionVisiblePopovers, DispatcherPriority.Background);
    }

    private void ToggleResetPopover()
    {
        _usagePopover.Hide();
        if (_resetPopover.IsVisible)
        {
            _resetPopover.Hide();
            return;
        }

        _resetPopover.Update(_viewModel);
        _resetPopover.ShowFor(this);
        Dispatcher.UIThread.Post(PositionVisiblePopovers, DispatcherPriority.Background);
    }

    private void ApplyViewModel()
    {
        _fiveHourText.Text = _viewModel.Usage?.FiveHour is { } five
            ? $"{L.Get("FiveHour")} {five.RemainingPercent}%"
            : $"{L.Get("FiveHour")} --";
        _weeklyText.Text = _viewModel.Usage?.Weekly is { } weekly
            ? $"W {weekly.RemainingPercent}%"
            : "W --";
        ToolTip.SetTip(
            _resetButton,
            _viewModel.LatestReset is { } latest
                ? L.Get("LastReset", HudViewModel.FormatAge(latest.AnnouncedAt, DateTimeOffset.UtcNow))
                : L.Get("ResetUnavailable"));
        _usagePopover.Update(_viewModel);
        _resetPopover.Update(_viewModel);
        ApplyTheme();
    }

    private void ApplyTheme()
    {
        var text = Color.Parse(_dark ? "#A6ABAF" : "#7E8489");
        _platformHud.ApplyTheme(this, _surfaceRoot, _target);
        _separatorText.Foreground = new SolidColorBrush(text);
        _fiveHourText.Foreground = MakeQuotaBrush(
            QuotaStatusEvaluator.EvaluateFiveHour(_viewModel.Usage?.FiveHour),
            text);
        _weeklyText.Foreground = MakeQuotaBrush(
            QuotaStatusEvaluator.EvaluateWeekly(_viewModel.Usage?.Weekly, DateTimeOffset.UtcNow),
            text);
        var latestResetAge = _viewModel.LatestReset is { } latest
            ? DateTimeOffset.UtcNow - latest.AnnouncedAt
            : TimeSpan.MaxValue;
        var recentReset = _viewModel.LatestResetIsFresh
            && latestResetAge >= TimeSpan.Zero
            && latestResetAge < TimeSpan.FromHours(12);
        _resetText.Foreground = new SolidColorBrush(
            recentReset ? Color.Parse(_dark ? "#65BE85" : "#3F9360") : text);
        foreach (var updateHighlight in _updateButtonHighlights)
        {
            updateHighlight();
        }
    }

    private SolidColorBrush MakeQuotaBrush(QuotaStatusLevel status, Color normal) => new(
        status switch
        {
            QuotaStatusLevel.Amber => Color.Parse(_dark ? "#E0A34A" : "#B87518"),
            QuotaStatusLevel.Red => Color.Parse(_dark ? "#E96B6B" : "#C84848"),
            _ => normal,
        });
}
