using System;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using NexSync.Desktop.Auth;
using NexSync.Desktop.Data;
using NexSync.Desktop.Sync;

namespace NexSync.Desktop;

public sealed class DesktopServices : IDisposable
{
    public AppState State { get; } = new();
    public DesktopAuthService Auth { get; }
    private readonly string _dataDirectory;

    private SyncEngine? _engine;
    private LocalDb? _engineDb;
    private string? _engineRoot;
    private CancellationTokenSource? _loopCts;
    private Task? _loopTask;
    private readonly SemaphoreSlim _wake = new(0, 1);
    private readonly SemaphoreSlim _cycleGate = new(1, 1);
    private int _recoveryNeeded;
    private Action? _recoveryHandler;
    private bool _disposed;

    // Loop cadence: first cycle runs immediately, then every 10s or on "Sync now".
    private static readonly TimeSpan SyncInterval = TimeSpan.FromSeconds(10);

    public DesktopServices(string? baseUrl = null, string? dataDirectory = null, HttpMessageHandler? handler = null)
    {
        var url = baseUrl ?? Environment.GetEnvironmentVariable("NEXSYNC_API_URL") ?? "http://localhost:5189";
        _dataDirectory = dataDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NexSync");
        Auth = handler is null
            ? new DesktopAuthService(url, _dataDirectory)
            : new DesktopAuthService(url, _dataDirectory, handler);
        State.Changed += OnStateChanged;
    }

    public SetupService ForLocalRoot(string localRoot) => SetupService.ForLocalRoot(localRoot);

    // One full sync cycle: recover if needed, pull remote changes, push queued local ops.
    // The gate keeps concurrent callers (loop + explicit runs) off the shared SQLite connection.
    public async Task RunSyncCycleAsync(CancellationToken ct = default)
    {
        await _cycleGate.WaitAsync(ct);
        try
        {
            var engine = _engine;
            if (engine is null) return;
            if (Interlocked.Exchange(ref _recoveryNeeded, 0) == 1)
            {
                var outcome = await engine.Recovery.ReconcileAsync(ct);
                // Offline at startup: keep the flag so the next cycle retries and
                // picks up files changed while the app was closed.
                if (outcome == ReconcileOutcome.TransientFailure)
                    Volatile.Write(ref _recoveryNeeded, 1);
            }
            var until = await FetchServerCursorAsync(ct);
            if (until is { } highWater)
                await engine.Pull.PullAndApplyAsync(highWater, ct);
            await engine.Coordinator.RunOnceAsync(ct);
        }
        finally
        {
            _cycleGate.Release();
        }
    }

    private async Task<long?> FetchServerCursorAsync(CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, "api/sync/cursor");
        using var res = await Auth.SendWithAuthAsync(req, allowRefresh: true, ct);
        if (!res.IsSuccessStatusCode) return null;
        using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync(ct));
        return doc.RootElement.GetProperty("highWaterCursor").GetInt64();
    }

    private async Task SyncLoopAsync(SyncEngine engine, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await RunSyncCycleAsync(ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch
            {
                // Offline or server error: retry on the next tick.
            }
            try
            {
                await _wake.WaitAsync(SyncInterval, ct);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private void OnStateChanged()
    {
        var root = State.LocalRoot;
        if (string.IsNullOrEmpty(root))
            StopEngine();
        else if (!string.Equals(_engineRoot, root, StringComparison.OrdinalIgnoreCase))
            StartEngine(root);
        // DesktopServices subscribes before any view, so the flag is still set here.
        if (State.SyncRequested && _engine is not null && _wake.CurrentCount == 0)
            _wake.Release();
    }

    private void StartEngine(string root)
    {
        StopEngine();
        var db = new LocalDb(Path.Combine(root, ".nexsync", "local.db"));
        var engine = new SyncEngine(db, Auth, root);
        _recoveryHandler = () => Volatile.Write(ref _recoveryNeeded, 1);
        engine.RecoveryRequired += _recoveryHandler;
        engine.Watcher.Start();
        _engine = engine;
        _engineDb = db;
        _engineRoot = root;
        // Reconcile on startup: the watcher only sees changes made while running,
        // so scan disk once to push files/folders added while the app was closed.
        Volatile.Write(ref _recoveryNeeded, 1);
        _loopCts = new CancellationTokenSource();
        var cts = _loopCts;
        _loopTask = Task.Run(() => SyncLoopAsync(engine, cts.Token));
    }

    private void StopEngine()
    {
        _loopCts?.Cancel();
        try
        {
            // Let the in-flight cycle finish on a live connection before disposing it.
            _loopTask?.Wait(TimeSpan.FromSeconds(2));
        }
        catch (AggregateException)
        {
            // Faulted loop: nothing to drain, proceed with teardown.
        }
        _loopCts?.Dispose();
        _loopCts = null;
        _loopTask = null;
        if (_engine is not null && _recoveryHandler is not null)
            _engine.RecoveryRequired -= _recoveryHandler;
        _engine?.Dispose();
        _engineDb?.Dispose();
        _engine = null;
        _engineDb = null;
        _engineRoot = null;
        _recoveryHandler = null;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        State.Changed -= OnStateChanged;
        StopEngine();
        _wake.Dispose();
        _cycleGate.Dispose();
        Auth.Dispose();
    }
}
