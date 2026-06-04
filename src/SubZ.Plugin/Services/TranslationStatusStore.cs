using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace SubZ.Plugin.Services;

public interface ITranslationStatusStore
{
    IReadOnlyList<TranslationTaskStatus> Load();
    void Save(IReadOnlyList<TranslationTaskStatus> statuses);
}

public sealed class FileTranslationStatusStore : ITranslationStatusStore
{
    private const int MaxPersistedStatuses = 500;
    private const string FileName = "subz-task-status.tsv";
    private const int LogFallbackDays = 30;

    public IReadOnlyList<TranslationTaskStatus> Load()
    {
        var path = GetStatusFilePath();
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return LoadFromRecentLogs();
        }

        var result = new List<TranslationTaskStatus>();
        foreach (var line in File.ReadAllLines(path))
        {
            var parts = line.Split('\t');
            if (parts.Length < 4)
            {
                continue;
            }

            if (!Enum.TryParse(parts[1], ignoreCase: true, out TranslationTaskState state))
            {
                continue;
            }

            if (!long.TryParse(parts[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out var ticks))
            {
                continue;
            }

            result.Add(new TranslationTaskStatus
            {
                Target = Decode(parts[0]),
                State = state,
                Message = Decode(parts[2]),
                UpdatedAt = new DateTimeOffset(ticks, TimeSpan.Zero)
            });
        }

        var statuses = result
            .Where(static s => !string.IsNullOrWhiteSpace(s.Target))
            .OrderByDescending(static s => s.UpdatedAt)
            .Take(MaxPersistedStatuses)
            .ToArray();

        return statuses.Length > 0 ? statuses : LoadFromRecentLogs();
    }

    public void Save(IReadOnlyList<TranslationTaskStatus> statuses)
    {
        var path = GetStatusFilePath();
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(dir))
        {
            Directory.CreateDirectory(dir);
        }

        var lines = (statuses ?? Array.Empty<TranslationTaskStatus>())
            .Where(static s => !string.IsNullOrWhiteSpace(s.Target))
            .OrderByDescending(static s => s.UpdatedAt)
            .Take(MaxPersistedStatuses)
            .Select(static s => string.Join(
                "\t",
                Encode(s.Target),
                s.State.ToString(),
                Encode(s.Message),
                s.UpdatedAt.UtcTicks.ToString(CultureInfo.InvariantCulture)))
            .ToArray();

        var temp = path + ".tmp";
        File.WriteAllLines(temp, lines);
        if (File.Exists(path))
        {
            File.Delete(path);
        }

        File.Move(temp, path);
    }

    private static string GetStatusFilePath()
    {
        return Path.Combine(FileRuntimeLogger.GetLogDirectory(), FileName);
    }

    private IReadOnlyList<TranslationTaskStatus> LoadFromRecentLogs()
    {
        var cutoff = DateTimeOffset.UtcNow.AddDays(-LogFallbackDays);
        var statuses = TranslationStatusLogParser.Parse(FileRuntimeLogger.ReadLinesSince(cutoff), cutoff)
            .Take(MaxPersistedStatuses)
            .ToArray();

        if (statuses.Length > 0)
        {
            Save(statuses);
        }

        return statuses;
    }

    private static string Encode(string? value)
    {
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(value ?? string.Empty));
    }

    private static string Decode(string value)
    {
        try
        {
            return Encoding.UTF8.GetString(Convert.FromBase64String(value ?? string.Empty));
        }
        catch
        {
            return string.Empty;
        }
    }
}
