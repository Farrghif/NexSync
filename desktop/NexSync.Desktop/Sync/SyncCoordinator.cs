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
    int batchSize = 50)
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private int _pendingSignals;

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
            var batch = queue.DequeueEligibleBatch(batchSize);
            var attempted = 0;
            var pushed = 0;
            var conflicts = 0;
            var stoppedByAuth = false;
            var needsReconcile = false;
            foreach (var op in batch)
            {
                ct.ThrowIfCancellationRequested();
                attempted++;
                var outcome = await push.PushAsync(op, ct);
                switch (outcome)
                {
                    case PushOutcome.Pushed:
                        pushed++;
                        break;
                    case PushOutcome.Conflict:
                        conflicts++;
                        break;
                    case PushOutcome.AuthFailed:
                    case PushOutcome.Forbidden:
                        stoppedByAuth = true;
                        return new PushBatchResult(attempted, pushed, conflicts, true, needsReconcile);
                    case PushOutcome.NeedsReconcile:
                        needsReconcile = true;
                        break;
                    case PushOutcome.TransientFailure:
                        break;
                }
            }
            return new PushBatchResult(attempted, pushed, conflicts, stoppedByAuth, needsReconcile);
        }
        finally
        {
            _gate.Release();
        }
    }

    public bool HasPendingSignal => Volatile.Read(ref _pendingSignals) > 0;
}
