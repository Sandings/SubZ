using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace SubZ.Plugin.Services;

public enum TranslationTaskState
{
    Queued,
    Running,
    Succeeded,
    Failed,
    Stopped
}

public sealed class TranslationTaskStatus
{
    public string Target { get; set; } = string.Empty;
    public TranslationTaskState State { get; set; } = TranslationTaskState.Queued;
    public string Message { get; set; } = string.Empty;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class TranslationRuntimeLogEntry
{
    public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;
    public string Level { get; set; } = "Info";
    public string Message { get; set; } = string.Empty;
}

public sealed class TranslationControlSnapshot
{
    public bool IsRunning { get; set; }
    public bool IsPaused { get; set; }
    public int QueuedCount { get; set; }
}

public sealed class InMemoryTranslationJobDispatcher : ITranslationJobDispatcher
{
    private const int MaxRuntimeLogs = 400;
    private static readonly ConcurrentQueue<TranslationRuntimeLogEntry> RuntimeLogs = new ConcurrentQueue<TranslationRuntimeLogEntry>();
    private static int _runtimeLogCount;

    private readonly ConcurrentQueue<string> _queue = new ConcurrentQueue<string>();
    private readonly ConcurrentDictionary<string, TranslationTaskStatus> _status = new ConcurrentDictionary<string, TranslationTaskStatus>(StringComparer.OrdinalIgnoreCase);
    private readonly Func<string, CancellationToken, Task<TranslationTargetResult>> _handler;
    private readonly ITranslationStatusStore _statusStore;
    private readonly object _gate = new object();
    private bool _workerRunning;
    private bool _isPaused;
    private CancellationTokenSource? _workerCts;
    private Task? _workerTask;


    public InMemoryTranslationJobDispatcher(Func<string, CancellationToken, Task<TranslationTargetResult>> handler, ITranslationStatusStore? statusStore = null)
    {
        _handler = handler;
        _statusStore = statusStore ?? new FileTranslationStatusStore();
        RestoreStatuses();
    }

    public Task EnqueueAsync(IEnumerable<string> targets, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        foreach (var target in targets.Where(static t => !string.IsNullOrWhiteSpace(t)))
        {
            var normalized = target.Trim();
            _queue.Enqueue(normalized);
            _status[normalized] = new TranslationTaskStatus
            {
                Target = normalized,
                State = TranslationTaskState.Queued,
                Message = UiText.Queued(),
                UpdatedAt = DateTimeOffset.UtcNow
            };

            AppendRuntimeLog("Info", UiText.QueuedTarget(normalized));
        }

        PersistStatuses();
        EnsureWorker();
        return Task.CompletedTask;
    }

    public IReadOnlyList<string> SnapshotQueuedTargets()
    {
        return _queue.ToArray();
    }

    public IReadOnlyList<TranslationTaskStatus> SnapshotStatuses()
    {
        return _status.Values.OrderByDescending(s => s.UpdatedAt).ToArray();
    }

    public TranslationControlSnapshot SnapshotControl()
    {
        lock (_gate)
        {
            return new TranslationControlSnapshot
            {
                IsRunning = _workerRunning,
                IsPaused = _isPaused,
                QueuedCount = _queue.Count
            };
        }
    }

    public IReadOnlyList<TranslationRuntimeLogEntry> SnapshotRuntimeLogs(int take = 120)
    {
        if (take <= 0)
        {
            take = 1;
        }

        return RuntimeLogs
            .ToArray()
            .OrderByDescending(e => e.Timestamp)
            .Take(take)
            .ToArray();
    }

    public static void AppendRuntimeLog(string level, string message)
    {
        var timestamp = DateTimeOffset.UtcNow;
        var entry = new TranslationRuntimeLogEntry
        {
            Timestamp = timestamp,
            Level = string.IsNullOrWhiteSpace(level) ? "Info" : level,
            Message = message ?? string.Empty
        };

        RuntimeLogs.Enqueue(entry);
        Interlocked.Increment(ref _runtimeLogCount);

        while (Volatile.Read(ref _runtimeLogCount) > MaxRuntimeLogs && RuntimeLogs.TryDequeue(out _))
        {
            Interlocked.Decrement(ref _runtimeLogCount);
        }

        try
        {
            FileRuntimeLogger.Write(timestamp, entry.Level, entry.Message);
        }
        catch
        {
            // Keep runtime flow alive even when file logging fails.
        }
    }

    public void Pause()
    {
        CancellationTokenSource? cts;
        lock (_gate)
        {
            _isPaused = true;
            cts = _workerCts;
        }

        cts?.Cancel();
        AppendRuntimeLog("Warn", UiText.DispatcherPaused());
    }

    public void Resume()
    {
        lock (_gate)
        {
            _isPaused = false;
        }

        AppendRuntimeLog("Info", UiText.DispatcherResumed());
        EnsureWorker();
    }

    public void StopAll()
    {
        CancellationTokenSource? cts;
        lock (_gate)
        {
            _isPaused = false;
            cts = _workerCts;
        }

        while (_queue.TryDequeue(out var queuedTarget))
        {
            var queuedStatus = _status.GetOrAdd(queuedTarget, t => new TranslationTaskStatus { Target = t });
            queuedStatus.State = TranslationTaskState.Stopped;
            queuedStatus.Message = UiText.StoppedByUser();
            queuedStatus.UpdatedAt = DateTimeOffset.UtcNow;
        }

        PersistStatuses();
        cts?.Cancel();
        AppendRuntimeLog("Warn", UiText.DispatcherStopRequested());
    }

    private void RestoreStatuses()
    {
        try
        {
            foreach (var restored in _statusStore.Load())
            {
                if (string.IsNullOrWhiteSpace(restored.Target))
                {
                    continue;
                }

                var state = restored.State;
                var message = restored.Message ?? string.Empty;
                if (state == TranslationTaskState.Queued || state == TranslationTaskState.Running)
                {
                    state = TranslationTaskState.Stopped;
                    message = "Restored from previous session.";
                }

                _status[restored.Target] = new TranslationTaskStatus
                {
                    Target = restored.Target,
                    State = state,
                    Message = message,
                    UpdatedAt = restored.UpdatedAt
                };
            }
        }
        catch (Exception ex)
        {
            AppendRuntimeLog("Warn", $"Failed to restore task statuses: {ex.Message}");
        }
    }

    private void PersistStatuses()
    {
        try
        {
            _statusStore.Save(SnapshotStatuses());
        }
        catch (Exception ex)
        {
            AppendRuntimeLog("Warn", $"Failed to persist task statuses: {ex.Message}");
        }
    }

    private void EnsureWorker()
    {
        Task? workerToObserve = null;
        lock (_gate)
        {
            if (_workerRunning)
            {
                return;
            }

            var cts = new CancellationTokenSource();
            _workerCts = cts;
            _workerRunning = true;
            // Run worker loop on thread pool so Enqueue/Save path returns immediately.
            _workerTask = Task.Run(() => ProcessLoopAsync(cts));
            workerToObserve = _workerTask;
        }

        // Observe faulted background task so exceptions are never silent.
        workerToObserve.ContinueWith(
            t =>
            {
                var baseException = t.Exception?.GetBaseException() ?? t.Exception;
                var message = baseException?.Message ?? "Unknown worker failure.";
                AppendRuntimeLog("Error", $"Dispatcher worker crashed: {message}");
            },
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    private bool IsPaused()
    {
        lock (_gate)
        {
            return _isPaused;
        }
    }

    private async Task ProcessLoopAsync(CancellationTokenSource cts)
    {
        try
        {
            while (true)
            {
                await WaitIfPausedAsync(cts.Token).ConfigureAwait(false);
                cts.Token.ThrowIfCancellationRequested();
                if (!_queue.TryDequeue(out var target))
                {
                    break;
                }

                var status = _status.GetOrAdd(target, t => new TranslationTaskStatus { Target = t });
                status.State = TranslationTaskState.Running;
                status.Message = UiText.Running();
                status.UpdatedAt = DateTimeOffset.UtcNow;
                PersistStatuses();
                AppendRuntimeLog("Info", UiText.RunningTarget(target));

                try
                {
                    var result = await _handler(target, cts.Token).ConfigureAwait(false);
                    if (result.SkippedAll)
                    {
                        _status.TryRemove(target, out _);
                        PersistStatuses();
                        AppendRuntimeLog("Info", $"Removed skipped target from task status: {target}");
                        continue;
                    }

                    status.State = TranslationTaskState.Succeeded;
                    status.Message = UiText.Completed();
                    status.UpdatedAt = DateTimeOffset.UtcNow;
                    PersistStatuses();
                    AppendRuntimeLog("Info", UiText.CompletedTarget(target));
                }
                catch (OperationCanceledException)
                {
                    if (_isPaused)
                    {
                        _queue.Enqueue(target);
                        status.State = TranslationTaskState.Queued;
                        status.Message = UiText.Paused();
                        status.UpdatedAt = DateTimeOffset.UtcNow;
                        PersistStatuses();
                        AppendRuntimeLog("Warn", UiText.PausedAndRequeuedTarget(target));
                    }
                    else
                    {
                        status.State = TranslationTaskState.Stopped;
                        status.Message = UiText.StoppedByUser();
                        status.UpdatedAt = DateTimeOffset.UtcNow;
                        PersistStatuses();
                        AppendRuntimeLog("Warn", UiText.StoppedTarget(target));
                    }

                    break;
                }
                catch (Exception ex)
                {
                    status.State = TranslationTaskState.Failed;
                    status.Message = ex.Message;
                    status.UpdatedAt = DateTimeOffset.UtcNow;
                    PersistStatuses();
                    AppendRuntimeLog("Error", UiText.FailedTarget(target, ex.Message));
                }
            }
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested)
        {
            // Expected path for pause/stop controls.
        }
        catch (Exception ex)
        {
            AppendRuntimeLog("Error", $"Dispatcher loop exception: {ex.Message}");
            throw;
        }
        finally
        {
            cts.Dispose();

            var shouldRestart = false;
            lock (_gate)
            {
                if (ReferenceEquals(_workerCts, cts))
                {
                    _workerCts = null;
                }

                _workerTask = null;
                _workerRunning = false;
                shouldRestart = !_queue.IsEmpty && !_isPaused;
            }

            if (shouldRestart)
            {
                EnsureWorker();
            }
        }
    }

    private async Task WaitIfPausedAsync(CancellationToken cancellationToken)
    {
        while (IsPaused())
        {
            cancellationToken.ThrowIfCancellationRequested();
            await Task.Delay(250, cancellationToken).ConfigureAwait(false);
        }
    }
}
