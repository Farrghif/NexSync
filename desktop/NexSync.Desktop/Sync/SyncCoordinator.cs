using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using NexSync.Desktop.Data;

namespace NexSync.Desktop.Sync;

public enum PushOutcome
{
    Pushed,
    Conflict,
    AuthFailed,
    Forbidden,
    NeedsReconcile,
    TransientFailure
}

public sealed record PushBatchResult(
    int Attempted,
    int Pushed,
    int Conflicts,
    bool StoppedByAuth,
    bool NeedsReconcile);

public sealed class SyncCoordinator(
    LocalDb db,
    PendingQueueService queue,
    PushPipeline push,
    int batchSize = 50,
    Func<TimeSpan, CancellationToken, Task>? sleeper = null,
    ConflictHandler? conflicts = null)
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private int _pendingSignals;
    private readonly Func<TimeSpan, CancellationToken, Task> _sleep =
        sleeper ?? ((delay, ct) => Task.Delay(delay, ct));

    public static TimeSpan ComputeBackoffDelay(int consecutiveFailures)
    {
        if (consecutiveFailures <= 0) return TimeSpan.Zero;
        var seconds = Math.Min(2 * (1 << Math.Min(consecutiveFailures - 1, 4)), 30);
        return TimeSpan.FromSeconds(seconds);
    }

    public void RequestSync() => Interlocked.Increment(ref _pendingSignals);

    public async Task<PushBatchResult> RunOnceAsync(CancellationToken ct = default)
    {
        if (!await _gate.WaitAsync(0, ct))
        {
            RequestSync();
            return new PushBatchResult(0, 0, 0, StoppedByAuth: false, NeedsReconcile: false);
        }
        try
        {
            Interlocked.Exchange(ref _pendingSignals, 0);
            queue.RequeueFailed();
            var batch = queue.DequeueEligibleBatch(batchSize);
            var attempted = 0;
            var pushed = 0;
            var conflictCount = 0;
            var stoppedByAuth = false;
            var needsReconcile = false;
            var consecutiveFailures = 0;
            foreach (var op in batch)
            {
                ct.ThrowIfCancellationRequested();
                if (consecutiveFailures > 0)
                    await _sleep(ComputeBackoffDelay(consecutiveFailures), ct);
                attempted++;
                var outcome = await push.PushAsync(op, ct);
                switch (outcome)
                {
                    case PushOutcome.Pushed:
                        pushed++;
                        consecutiveFailures = 0;
                        break;
                    case PushOutcome.Conflict:
                        conflictCount++;
                        consecutiveFailures = 0;
                        conflicts?.PreservePushConflict(op);
                        break;
                    case PushOutcome.AuthFailed:
                    case PushOutcome.Forbidden:
                        stoppedByAuth = true;
                        return new PushBatchResult(attempted, pushed, conflictCount, true, needsReconcile);
                    case PushOutcome.NeedsReconcile:
                        needsReconcile = true;
                        consecutiveFailures = 0;
                        break;
                    case PushOutcome.TransientFailure:
                        consecutiveFailures++;
                        break;
                }
            }
            return new PushBatchResult(attempted, pushed, conflictCount, stoppedByAuth, needsReconcile);
        }
        finally
        {
            _gate.Release();
        }
    }

    public bool HasPendingSignal => Volatile.Read(ref _pendingSignals) > 0;
}
