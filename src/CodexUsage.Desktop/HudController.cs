using Avalonia.Threading;
using CodexUsage.Core.Reset;
using CodexUsage.Core.Ui;
using CodexUsage.Core.Usage;
using CodexUsage.Desktop.Ui;

namespace CodexUsage.Desktop;

internal sealed class HudController : IAsyncDisposable
{
    private readonly HudViewModel _viewModel;
    private readonly TitlebarHudWindow _window;
    private readonly CodexRateLimitsClient _usageClient = new();
    private readonly ResetStateResolver _resolver = new();
    private readonly CompanionStateStore _stateStore = new();
    private readonly CancellationTokenSource _cancellation = new();
    private readonly SemaphoreSlim _saveGate = new(1, 1);
    private readonly object _gate = new();
    private readonly object _backgroundGate = new();
    private readonly HashSet<Task> _backgroundTasks = [];
    private CodexResetsClient? _resetClient;
    private PersistedCompanionState _state = new();
    private PublicResetFetch _publicReset = new(false, null, null);
    private UsageSnapshot? _currentUsage;
    private Task _usageLoop = Task.CompletedTask;
    private Task _resetLoop = Task.CompletedTask;

    public HudController(HudViewModel viewModel, TitlebarHudWindow window)
    {
        _viewModel = viewModel;
        _window = window;
    }

    public async Task StartAsync()
    {
        _state = await _stateStore.ReadAsync(_cancellation.Token).ConfigureAwait(false);
        _currentUsage = _state.UsageHistory?.LastOrDefault();
        _publicReset = new PublicResetFetch(
            false,
            _state.LastPublicReset,
            _state.LastPublicResetSuccessAt);
        _resetClient = new CodexResetsClient(_state.LastPublicReset, _state.LastPublicResetSuccessAt);

        _usageClient.SnapshotChanged += OnUsageChanged;
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            _viewModel.SetUsage(_currentUsage);
            _viewModel.SetLatestReset(_publicReset.Status?.Data.LatestReset, isFresh: false);
            RecomputeResetOnUiThread();
        });

        _usageLoop = RunUsageLoopAsync(_cancellation.Token);
        _resetLoop = RunResetLoopAsync(_cancellation.Token);
    }

    private async Task RunUsageLoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _usageClient.StartAsync(cancellationToken).ConfigureAwait(false);
            using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1));
            while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
            {
                await _usageClient.RefreshAsync(cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch
        {
            Dispatcher.UIThread.Post(() => _viewModel.SetUsage(null));
        }
    }

    private async Task RunResetLoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var fetch = await _resetClient!.ReadAsync(cancellationToken).ConfigureAwait(false);
                lock (_gate)
                {
                    _publicReset = fetch;
                    if (fetch.IsFresh && fetch.Status is not null)
                    {
                        _state = _state with
                        {
                            LastPublicReset = fetch.Status,
                            LastPublicResetSuccessAt = fetch.LastSuccessfulAt,
                        };
                    }
                }

                Dispatcher.UIThread.Post(() =>
                {
                    _viewModel.SetLatestReset(fetch.Status?.Data.LatestReset, fetch.IsFresh);
                    RecomputeResetOnUiThread();
                });
                await SaveStateAsync(cancellationToken).ConfigureAwait(false);
                await Task.Delay(TimeSpan.FromMinutes(5), cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private void OnUsageChanged(object? sender, UsageSnapshot snapshot)
    {
        lock (_gate)
        {
            _currentUsage = snapshot;
            var history = (_state.UsageHistory ?? []).
                Where(item => snapshot.ObservedAt - item.ObservedAt <= TimeSpan.FromHours(2))
                .Append(snapshot)
                .TakeLast(180)
                .ToArray();
            _state = _state with { UsageHistory = history };
        }

        Dispatcher.UIThread.Post(() =>
        {
            _viewModel.SetUsage(snapshot);
            RecomputeResetOnUiThread();
        });
        TrackBackground(SaveStateAsync(_cancellation.Token));
    }

    private void TrackBackground(Task task)
    {
        lock (_backgroundGate)
        {
            _backgroundTasks.Add(task);
        }

        _ = task.ContinueWith(
            completedTask =>
            {
                lock (_backgroundGate)
                {
                    _backgroundTasks.Remove(completedTask);
                }
            },
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    private void RecomputeResetOnUiThread()
    {
        ResetResolution resolution;
        lock (_gate)
        {
            var previous = SelectComparisonSnapshot();
            resolution = _resolver.Resolve(
                _publicReset,
                previous,
                _currentUsage,
                _state,
                DateTimeOffset.UtcNow);

            if (resolution.State == ResetVisualState.Confirmed)
            {
                _state = _state with
                {
                    ConfirmedEventId = resolution.ConfirmedEventId,
                    ConfirmedAt = resolution.ConfirmedAt,
                    ConfirmedUsage = resolution.ConfirmedUsage,
                };
            }
        }

        _viewModel.SetReset(resolution);
    }

    private UsageSnapshot? SelectComparisonSnapshot()
    {
        var history = _state.UsageHistory;
        if (history is null || history.Count < 2)
        {
            return null;
        }

        var announcement = _publicReset.Status?.Data.LatestReset;
        if (announcement is not null)
        {
            return history.LastOrDefault(item => item.ObservedAt <= announcement.AnnouncedAt)
                ?? history.First();
        }

        return history[^2];
    }

    private async Task SaveStateAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _saveGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            PersistedCompanionState state;
            lock (_gate)
            {
                state = _state;
            }

            await _stateStore.WriteAsync(state, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
        catch
        {
        }
        finally
        {
            if (_saveGate.CurrentCount == 0)
            {
                _saveGate.Release();
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        _usageClient.SnapshotChanged -= OnUsageChanged;
        _cancellation.Cancel();
        try
        {
            await Task.WhenAll(_usageLoop, _resetLoop).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }

        Task[] backgroundTasks;
        lock (_backgroundGate)
        {
            backgroundTasks = [.. _backgroundTasks];
        }
        try
        {
            await Task.WhenAll(backgroundTasks).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }

        _resetClient?.Dispose();
        await _usageClient.DisposeAsync().ConfigureAwait(false);
        _saveGate.Dispose();
        _cancellation.Dispose();
    }
}
