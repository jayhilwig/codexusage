using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Text.Json;

namespace CodexUsage.Core.Usage;

public sealed class CodexRateLimitsClient : ICodexRateLimitsSource
{
    private readonly string _codexExecutable;
    private readonly SemaphoreSlim _startGate = new(1, 1);
    private readonly SemaphoreSlim _refreshGate = new(1, 1);
    private readonly SemaphoreSlim _writeGate = new(1, 1);
    private readonly ConcurrentDictionary<long, TaskCompletionSource<JsonElement>> _pending = new();
    private Process? _process;
    private StreamWriter? _input;
    private CancellationTokenSource? _processCancellation;
    private Task? _readLoop;
    private Task _notificationRefreshTask = Task.CompletedTask;
    private readonly CancellationTokenSource _lifetimeCancellation = new();
    private long _requestId;
    private int _notificationRefreshScheduled;
    private bool _available;

    public CodexRateLimitsClient(string? codexExecutable = null)
    {
        _codexExecutable = ResolveCodexExecutable(codexExecutable);
    }

    public event EventHandler<UsageSnapshot>? SnapshotChanged;
    public event EventHandler<bool>? AvailabilityChanged;

    public UsageSnapshot? Current { get; private set; }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await EnsureStartedAsync(cancellationToken).ConfigureAwait(false);
        await RefreshAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<UsageSnapshot?> RefreshAsync(CancellationToken cancellationToken)
    {
        await _refreshGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await EnsureStartedAsync(cancellationToken).ConfigureAwait(false);
            var response = await RequestAsync("account/rateLimits/read", null, cancellationToken)
                .ConfigureAwait(false);
            var snapshot = ParseSnapshot(response);
            Current = snapshot;
            SetAvailability(true);
            SnapshotChanged?.Invoke(this, snapshot);
            return snapshot;
        }
        catch
        {
            SetAvailability(false);
            return null;
        }
        finally
        {
            _refreshGate.Release();
        }
    }

    private async Task EnsureStartedAsync(CancellationToken cancellationToken)
    {
        if (_process is { HasExited: false } && _input is not null)
        {
            return;
        }

        await _startGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_process is { HasExited: false } && _input is not null)
            {
                return;
            }

            await StopProcessAsync().ConfigureAwait(false);

            var processCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = _codexExecutable,
                    Arguments = "app-server --stdio",
                    RedirectStandardInput = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WindowStyle = ProcessWindowStyle.Hidden,
                },
                EnableRaisingEvents = true,
            };

            if (!process.Start())
            {
                throw new InvalidOperationException("Unable to start Codex app-server.");
            }

            _process = process;
            _input = process.StandardInput;
            _processCancellation = processCancellation;
            _readLoop = ReadLoopAsync(process, processCancellation.Token);
            _ = DrainErrorsAsync(process, processCancellation.Token);

            await RequestAsync(
                "initialize",
                new
                {
                    clientInfo = new { name = "codex-titlebar-hud", version = "0.1.0" },
                    capabilities = new { experimentalApi = true },
                },
                cancellationToken).ConfigureAwait(false);

            await SendAsync(new { method = "initialized", @params = new { } }, cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            _startGate.Release();
        }
    }

    private async Task<JsonElement> RequestAsync(
        string method,
        object? parameters,
        CancellationToken cancellationToken)
    {
        var id = Interlocked.Increment(ref _requestId);
        var completion = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending[id] = completion;

        try
        {
            object message = parameters is null
                ? new { method, id }
                : new { method, id, @params = parameters };
            await SendAsync(message, cancellationToken).ConfigureAwait(false);
            return await completion.Task.WaitAsync(TimeSpan.FromSeconds(15), cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            _pending.TryRemove(id, out _);
        }
    }

    private async Task SendAsync(object message, CancellationToken cancellationToken)
    {
        var input = _input ?? throw new InvalidOperationException("Codex app-server is not running.");
        var json = JsonSerializer.Serialize(message);
        await _writeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await input.WriteLineAsync(json.AsMemory(), cancellationToken).ConfigureAwait(false);
            await input.FlushAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _writeGate.Release();
        }
    }

    private async Task ReadLoopAsync(Process process, CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var line = await process.StandardOutput.ReadLineAsync(cancellationToken).ConfigureAwait(false);
                if (line is null)
                {
                    break;
                }

                using var document = JsonDocument.Parse(line);
                var root = document.RootElement;
                if (root.TryGetProperty("id", out var idElement) && idElement.TryGetInt64(out var id))
                {
                    if (!_pending.TryGetValue(id, out var completion))
                    {
                        continue;
                    }

                    if (root.TryGetProperty("error", out var error))
                    {
                        completion.TrySetException(new InvalidOperationException(
                            error.TryGetProperty("message", out var message)
                                ? message.GetString()
                                : "Codex app-server request failed."));
                    }
                    else if (root.TryGetProperty("result", out var result))
                    {
                        completion.TrySetResult(result.Clone());
                    }
                }
                else if (root.TryGetProperty("method", out var method)
                    && method.GetString() == "account/rateLimits/updated")
                {
                    ScheduleNotificationRefresh();
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch
        {
            SetAvailability(false);
        }
        finally
        {
            foreach (var completion in _pending.Values)
            {
                completion.TrySetException(new IOException("Codex app-server disconnected."));
            }
        }
    }

    private void ScheduleNotificationRefresh()
    {
        if (Interlocked.Exchange(ref _notificationRefreshScheduled, 1) != 0)
        {
            return;
        }

        _notificationRefreshTask = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(250, _lifetimeCancellation.Token).ConfigureAwait(false);
                await RefreshAsync(_lifetimeCancellation.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
            finally
            {
                Interlocked.Exchange(ref _notificationRefreshScheduled, 0);
            }
        });
    }

    private static string ResolveCodexExecutable(string? requestedExecutable)
    {
        if (!string.IsNullOrWhiteSpace(requestedExecutable))
        {
            return requestedExecutable;
        }

        var configuredExecutable = Environment.GetEnvironmentVariable("CODEX_HUD_CODEX_PATH");
        if (!string.IsNullOrWhiteSpace(configuredExecutable))
        {
            return configuredExecutable;
        }

        if (OperatingSystem.IsMacOS())
        {
            foreach (var candidate in new[]
            {
                "/Applications/Codex.app/Contents/Resources/codex",
                "/Applications/ChatGPT.app/Contents/Resources/codex",
            })
            {
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }
        }

        if (OperatingSystem.IsWindows())
        {
            try
            {
                var binRoot = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "OpenAI",
                    "Codex",
                    "bin");
                if (Directory.Exists(binRoot))
                {
                    var installedExecutable = Directory
                        .EnumerateFiles(binRoot, "codex.exe", SearchOption.AllDirectories)
                        .OrderByDescending(File.GetLastWriteTimeUtc)
                        .FirstOrDefault();
                    if (installedExecutable is not null)
                    {
                        return installedExecutable;
                    }
                }
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }

        return "codex";
    }

    private static UsageSnapshot ParseSnapshot(JsonElement result)
    {
        var rateLimits = result.GetProperty("rateLimits");
        JsonElement? codexRateLimits = null;
        if (result.TryGetProperty("rateLimitsByLimitId", out var byId)
            && byId.ValueKind == JsonValueKind.Object
            && byId.TryGetProperty("codex", out var codex))
        {
            codexRateLimits = codex;
            rateLimits = codex;
        }

        var primary = ReadWindow(rateLimits, "primary");
        var secondary = ReadWindow(rateLimits, "secondary");
        var windows = new[] { primary, secondary }.Where(window => window is not null).ToArray();

        var fiveHour = windows.FirstOrDefault(window => window!.WindowDurationMinutes == 300) ?? primary;
        var weekly = windows.FirstOrDefault(window => window!.WindowDurationMinutes == 10_080) ?? secondary;
        return new UsageSnapshot(
            fiveHour,
            weekly,
            DateTimeOffset.UtcNow,
            ReadCredits(result.GetProperty("rateLimits"), codexRateLimits));
    }

    private static CreditBalance? ReadCredits(JsonElement rateLimits, JsonElement? codexRateLimits)
    {
        var credits = codexRateLimits is { } codex
            && codex.TryGetProperty("credits", out var codexCredits)
            ? codexCredits
            : rateLimits.TryGetProperty("credits", out var limitCredits)
                ? limitCredits
                : default;
        if (credits.ValueKind != JsonValueKind.Object
            || (credits.TryGetProperty("unlimited", out var unlimited)
                && unlimited.ValueKind == JsonValueKind.True)
            || !credits.TryGetProperty("balance", out var balance)
            || !TryReadBalance(balance, out var value))
        {
            return null;
        }

        return new CreditBalance(value);
    }

    private static bool TryReadBalance(JsonElement balance, out decimal value)
    {
        value = default;
        if (balance.ValueKind == JsonValueKind.Number)
        {
            return balance.TryGetDecimal(out value);
        }

        return balance.ValueKind == JsonValueKind.String
            && !string.IsNullOrWhiteSpace(balance.GetString())
            && decimal.TryParse(
                balance.GetString(),
                NumberStyles.Number,
                CultureInfo.InvariantCulture,
                out value);
    }

    private static UsageWindow? ReadWindow(JsonElement rateLimits, string name)
    {
        if (!rateLimits.TryGetProperty(name, out var window) || window.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var used = window.GetProperty("usedPercent").GetInt32();
        long? duration = window.TryGetProperty("windowDurationMins", out var durationElement)
            && durationElement.ValueKind == JsonValueKind.Number
                ? durationElement.GetInt64()
                : null;
        DateTimeOffset? resetsAt = window.TryGetProperty("resetsAt", out var resetElement)
            && resetElement.ValueKind == JsonValueKind.Number
                ? DateTimeOffset.FromUnixTimeSeconds(resetElement.GetInt64())
                : null;
        return new UsageWindow(Math.Clamp(100 - used, 0, 100), used, resetsAt, duration);
    }

    private async Task DrainErrorsAsync(Process process, CancellationToken cancellationToken)
    {
        try
        {
            while (await process.StandardError.ReadLineAsync(cancellationToken).ConfigureAwait(false) is not null)
            {
                // Intentionally discarded. App-server diagnostics can contain local paths and are
                // not required for the HUD. Authentication material is never logged.
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private void SetAvailability(bool value)
    {
        if (_available == value)
        {
            return;
        }

        _available = value;
        AvailabilityChanged?.Invoke(this, value);
    }

    private async Task StopProcessAsync()
    {
        _processCancellation?.Cancel();
        if (_input is not null)
        {
            try { _input.Close(); } catch { }
        }

        if (_process is { HasExited: false } process)
        {
            try
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync().ConfigureAwait(false);
            }
            catch
            {
            }
        }

        _process?.Dispose();
        _processCancellation?.Dispose();
        _process = null;
        _input = null;
        _processCancellation = null;
        _readLoop = null;
    }

    public async ValueTask DisposeAsync()
    {
        _lifetimeCancellation.Cancel();
        try
        {
            await _notificationRefreshTask.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }

        await StopProcessAsync().ConfigureAwait(false);
        _startGate.Dispose();
        _refreshGate.Dispose();
        _writeGate.Dispose();
        _lifetimeCancellation.Dispose();
    }
}
