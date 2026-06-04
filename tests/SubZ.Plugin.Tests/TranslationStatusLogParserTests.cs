using SubZ.Plugin.Services;

namespace SubZ.Plugin.Tests;

public sealed class TranslationStatusLogParserTests
{
    [Fact]
    public void ParseRestoresLatestStatusPerTargetFromRecentLogLines()
    {
        var lines = new[]
        {
            "2026-05-01T00:00:00.0000000+00:00 [Info] Completed: /media/old.mkv",
            "2026-06-01T10:00:00.0000000+00:00 [Info] Queued target: /media/a.mkv",
            "2026-06-01T10:01:00.0000000+00:00 [Info] Running: /media/a.mkv",
            "2026-06-01T10:02:00.0000000+00:00 [Info] Completed: /media/a.mkv",
            "2026-06-01T10:03:00.0000000+00:00 [Error] Failed: /media/b.mkv | Missing subtitle",
            "2026-06-01T10:04:00.0000000+00:00 [Warn] Paused and re-queued: /media/c.mkv"
        };

        var statuses = TranslationStatusLogParser.Parse(
            lines,
            DateTimeOffset.Parse("2026-05-15T00:00:00Z"));

        Assert.Equal(3, statuses.Count);
        var a = statuses.Single(s => s.Target == "/media/a.mkv");
        Assert.Equal(TranslationTaskState.Succeeded, a.State);
        Assert.Equal("Completed", a.Message);

        var b = statuses.Single(s => s.Target == "/media/b.mkv");
        Assert.Equal(TranslationTaskState.Failed, b.State);
        Assert.Equal("Missing subtitle", b.Message);

        var c = statuses.Single(s => s.Target == "/media/c.mkv");
        Assert.Equal(TranslationTaskState.Queued, c.State);
        Assert.Equal("Paused", c.Message);
    }

    [Fact]
    public void ParseDoesNotRestoreTargetsThatOnlyHaveSkipLogs()
    {
        var lines = new[]
        {
            "2026-06-01T10:00:00.0000000+00:00 [Info] Queued target: /media/skipped-folder",
            "2026-06-01T10:01:00.0000000+00:00 [Info] Running: /media/skipped-folder",
            "2026-06-01T10:02:00.0000000+00:00 [Info] Skip existing embedded target subtitle: /media/skipped-folder/a.mkv -> zh-CN",
            "2026-06-01T10:03:00.0000000+00:00 [Info] Removed skipped target from task status: /media/skipped-folder"
        };

        var statuses = TranslationStatusLogParser.Parse(
            lines,
            DateTimeOffset.Parse("2026-05-15T00:00:00Z"));

        Assert.Empty(statuses);
    }
}
