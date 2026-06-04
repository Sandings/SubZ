using SubZ.Plugin.Services;

namespace SubZ.Plugin.Tests;

public sealed class TranslationStatusPersistenceTests
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), "subz_status_tests_" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void ConstructorRestoresPreviousStatusesFromStore()
    {
        var updated = DateTimeOffset.Parse("2026-06-02T09:00:00Z");
        var store = new FakeStatusStore(new[]
        {
            new TranslationTaskStatus
            {
                Target = "/media/show/s01e01.mkv",
                State = TranslationTaskState.Succeeded,
                Message = "Completed",
                UpdatedAt = updated
            }
        });

        var dispatcher = new InMemoryTranslationJobDispatcher(
            (_, _) => Task.FromResult(TranslationTargetResult.Completed),
            store);

        var status = Assert.Single(dispatcher.SnapshotStatuses());
        Assert.Equal("/media/show/s01e01.mkv", status.Target);
        Assert.Equal(TranslationTaskState.Succeeded, status.State);
        Assert.Equal("Completed", status.Message);
        Assert.Equal(updated, status.UpdatedAt);
    }

    [Theory]
    [InlineData(TranslationTaskState.Queued)]
    [InlineData(TranslationTaskState.Running)]
    public void ConstructorMarksTransientPreviousStatusesAsStopped(TranslationTaskState state)
    {
        var store = new FakeStatusStore(new[]
        {
            new TranslationTaskStatus
            {
                Target = "/media/show/s01e02.mkv",
                State = state,
                Message = state.ToString(),
                UpdatedAt = DateTimeOffset.Parse("2026-06-02T09:10:00Z")
            }
        });

        var dispatcher = new InMemoryTranslationJobDispatcher(
            (_, _) => Task.FromResult(TranslationTargetResult.Completed),
            store);

        var status = Assert.Single(dispatcher.SnapshotStatuses());
        Assert.Equal(TranslationTaskState.Stopped, status.State);
        Assert.Equal("Restored from previous session.", status.Message);
    }

    [Fact]
    public async Task EnqueuePersistsQueuedStatus()
    {
        var store = new FakeStatusStore(Array.Empty<TranslationTaskStatus>());
        var dispatcher = new InMemoryTranslationJobDispatcher(
            (_, _) => Task.FromResult(TranslationTargetResult.Completed),
            store);
        dispatcher.Pause();

        await dispatcher.EnqueueAsync(new[] { "/media/show/s01e03.mkv" }, CancellationToken.None);

        var status = Assert.Single(store.SavedStatuses);
        Assert.Equal("/media/show/s01e03.mkv", status.Target);
        Assert.Equal(TranslationTaskState.Queued, status.State);
    }

    [Fact]
    public async Task SkippedTargetIsRemovedFromStatusAndPersistence()
    {
        var store = new FakeStatusStore(Array.Empty<TranslationTaskStatus>());
        var dispatcher = new InMemoryTranslationJobDispatcher(
            (_, _) => Task.FromResult(TranslationTargetResult.Skipped),
            store);

        await dispatcher.EnqueueAsync(new[] { "/media/movie-with-existing-subs" }, CancellationToken.None);
        await WaitUntil(() => dispatcher.SnapshotControl().IsRunning == false, TimeSpan.FromSeconds(3));

        Assert.Empty(dispatcher.SnapshotStatuses());
        Assert.Empty(store.SavedStatuses);
    }

    [Fact]
    public void FileStoreFallsBackToRecentRuntimeLogsWhenStatusFileIsEmpty()
    {
        Directory.CreateDirectory(_tempDir);
        FileRuntimeLogger.Configure(_tempDir, 10, 90);
        var logDir = Path.Combine(_tempDir, "logs");
        Directory.CreateDirectory(logDir);
        File.WriteAllLines(
            Path.Combine(logDir, "subz-runtime-20260602.log"),
            new[]
            {
                "2026-04-20T00:00:00.0000000+00:00 [Info] Completed: /media/too-old.mkv",
                DateTimeOffset.UtcNow.AddDays(-2).ToString("O") + " [Info] Completed: /media/recent.mkv"
            });

        var store = new FileTranslationStatusStore();

        var statuses = store.Load();

        var status = statuses.Single(s => s.Target == "/media/recent.mkv");
        Assert.Equal("/media/recent.mkv", status.Target);
        Assert.Equal(TranslationTaskState.Succeeded, status.State);
        Assert.DoesNotContain(statuses, s => s.Target == "/media/too-old.mkv");
    }

    private sealed class FakeStatusStore : ITranslationStatusStore
    {
        private readonly IReadOnlyList<TranslationTaskStatus> _loadedStatuses;

        public FakeStatusStore(IReadOnlyList<TranslationTaskStatus> loadedStatuses)
        {
            _loadedStatuses = loadedStatuses;
        }

        public IReadOnlyList<TranslationTaskStatus> SavedStatuses { get; private set; } = Array.Empty<TranslationTaskStatus>();

        public IReadOnlyList<TranslationTaskStatus> Load()
        {
            return _loadedStatuses;
        }

        public void Save(IReadOnlyList<TranslationTaskStatus> statuses)
        {
            SavedStatuses = statuses.Select(s => new TranslationTaskStatus
            {
                Target = s.Target,
                State = s.State,
                Message = s.Message,
                UpdatedAt = s.UpdatedAt
            }).ToArray();
        }
    }

    private static async Task WaitUntil(Func<bool> predicate, TimeSpan timeout)
    {
        var deadline = DateTimeOffset.UtcNow.Add(timeout);
        while (DateTimeOffset.UtcNow < deadline)
        {
            if (predicate())
            {
                return;
            }

            await Task.Delay(25);
        }
    }
}
