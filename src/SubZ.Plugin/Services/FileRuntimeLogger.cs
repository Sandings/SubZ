using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace SubZ.Plugin.Services;

public static class FileRuntimeLogger
{
    private const string PreferredLinuxLogDirectory = "/config/plugins/SubZ.Plugin/logs";
    private static readonly object SyncRoot = new object();
    private const string LogDirName = "logs";
    private const string LogBaseName = "subz-runtime";
    private const string LogExt = ".log";

    private static string? _logDirectory;
    private static int _maxSizeBytes = 10 * 1024 * 1024;
    private static int _retentionDays = 7;
    private static DateTimeOffset _nextPruneAt = DateTimeOffset.MinValue;
    private static int _writesSincePrune;
    private const int PruneEveryWrites = 100;
    private static readonly TimeSpan PruneInterval = TimeSpan.FromMinutes(5);

    public static void Configure(string? baseDirectory, int maxSizeMb, int retentionDays)
    {
        lock (SyncRoot)
        {
            var root = (baseDirectory ?? string.Empty).Trim();
            if (root.Length == 0)
            {
                root = AppContext.BaseDirectory;
            }

            _logDirectory = Path.Combine(root, LogDirName);
            Directory.CreateDirectory(_logDirectory);

            _maxSizeBytes = Math.Max(1, maxSizeMb) * 1024 * 1024;
            _retentionDays = Math.Max(1, retentionDays);
            _writesSincePrune = 0;
            _nextPruneAt = DateTimeOffset.MinValue;
        }
    }

    public static void Write(DateTimeOffset timestamp, string level, string message)
    {
        lock (SyncRoot)
        {
            EnsureConfigured();
            var path = GetCurrentLogPath(timestamp);
            RotateIfNeeded(path);

            var line = string.Format(
                CultureInfo.InvariantCulture,
                "{0:O} [{1}] {2}{3}",
                timestamp,
                string.IsNullOrWhiteSpace(level) ? "Info" : level.Trim(),
                message ?? string.Empty,
                Environment.NewLine);

            File.AppendAllText(path, line, new UTF8Encoding(false));
            MaybePruneOldLogs(timestamp);
        }
    }

    public static IReadOnlyList<string> ReadLastLines(int take)
    {
        return ReadLastLines(take, newestFirst: false);
    }

    public static IReadOnlyList<string> ReadLastLines(int take, bool newestFirst)
    {
        lock (SyncRoot)
        {
            EnsureConfigured();

            take = Math.Max(1, take);
            var files = GetReadableLogFiles()
                .OrderByDescending(static f => f, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            if (files.Length == 0)
            {
                return Array.Empty<string>();
            }

            var lines = new List<string>(take);
            foreach (var file in files)
            {
                var fileLines = TailLines(file, take - lines.Count);
                if (fileLines.Count > 0)
                {
                    lines.InsertRange(0, fileLines);
                }

                if (lines.Count >= take)
                {
                    break;
                }
            }

            var result = lines.Count <= take
                ? lines.ToArray()
                : lines.Skip(lines.Count - take).ToArray();

            return newestFirst
                ? result.Reverse().ToArray()
                : result;
        }
    }

    public static IReadOnlyList<string> ReadAllTokenUsageLines()
    {
        lock (SyncRoot)
        {
            EnsureConfigured();

            var lines = new List<string>();
            var files = GetReadableLogFiles()
                .OrderBy(static f => f, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            foreach (var file in files)
            {
                using (var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                using (var reader = new StreamReader(stream, Encoding.UTF8, true))
                {
                    while (!reader.EndOfStream)
                    {
                        var line = reader.ReadLine();
                        if (line != null && line.IndexOf("Token usage |", StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            lines.Add(line);
                        }
                    }
                }
            }

            return lines;
        }
    }

    public static IReadOnlyList<string> ReadLinesSince(DateTimeOffset cutoffUtc)
    {
        lock (SyncRoot)
        {
            EnsureConfigured();

            var lines = new List<string>();
            var files = GetReadableLogFiles()
                .OrderBy(static f => f, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            foreach (var file in files)
            {
                using (var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                using (var reader = new StreamReader(stream, Encoding.UTF8, true))
                {
                    while (!reader.EndOfStream)
                    {
                        var line = reader.ReadLine();
                        if (line == null)
                        {
                            continue;
                        }

                        var firstSpace = line.IndexOf(' ');
                        if (firstSpace <= 0)
                        {
                            continue;
                        }

                        if (DateTimeOffset.TryParse(
                                line.Substring(0, firstSpace),
                                CultureInfo.InvariantCulture,
                                DateTimeStyles.AssumeUniversal,
                                out var timestamp)
                            && timestamp.ToUniversalTime() >= cutoffUtc.ToUniversalTime())
                        {
                            lines.Add(line);
                        }
                    }
                }
            }

            return lines;
        }
    }

    public static string GetLogDirectory()
    {
        lock (SyncRoot)
        {
            EnsureConfigured();
            return _logDirectory!;
        }
    }

    public static IReadOnlyList<string> GetReadableLogDirectories()
    {
        lock (SyncRoot)
        {
            EnsureConfigured();
            return GetCandidateLogDirectories().ToArray();
        }
    }

    private static void EnsureConfigured()
    {
        if (!string.IsNullOrWhiteSpace(_logDirectory))
        {
            return;
        }

        Configure(AppContext.BaseDirectory, 10, 7);
    }

    private static IEnumerable<string> GetReadableLogFiles()
    {
        foreach (var dir in GetCandidateLogDirectories())
        {
            if (!Directory.Exists(dir))
            {
                continue;
            }

            foreach (var file in Directory.GetFiles(dir, $"{LogBaseName}-*{LogExt}"))
            {
                yield return file;
            }
        }
    }

    private static IEnumerable<string> GetCandidateLogDirectories()
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var directory in GetCandidateLogDirectoriesCore())
        {
            if (string.IsNullOrWhiteSpace(directory))
            {
                continue;
            }

            var normalized = directory.Trim().TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (normalized.Length == 0)
            {
                continue;
            }

            if (seen.Add(normalized))
            {
                yield return normalized;
            }
        }
    }

    private static IEnumerable<string> GetCandidateLogDirectoriesCore()
    {
        yield return PreferredLinuxLogDirectory;

        if (!string.IsNullOrWhiteSpace(_logDirectory))
        {
            yield return _logDirectory!;
        }

        var baseDir = AppContext.BaseDirectory;
        if (!string.IsNullOrWhiteSpace(baseDir))
        {
            yield return Path.Combine(baseDir, LogDirName);
            yield return Path.Combine(baseDir, "plugins", "SubZ.Plugin", LogDirName);

            var parent = Path.GetDirectoryName(baseDir);
            if (!string.IsNullOrWhiteSpace(parent))
            {
                yield return Path.Combine(parent, "plugins", "SubZ.Plugin", LogDirName);
            }
        }

        // Backward-compatible fallback for old deployments.
        yield return "/config/plugins/configurations/SubZ/logs";
        yield return "/mnt/user/DockerFile/emby/plugins/SubZ.Plugin/logs";
    }

    private static string GetCurrentLogPath(DateTimeOffset now)
    {
        var date = now.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
        return Path.Combine(_logDirectory!, $"{LogBaseName}-{date}{LogExt}");
    }

    private static void RotateIfNeeded(string path)
    {
        if (!File.Exists(path))
        {
            return;
        }

        var info = new FileInfo(path);
        if (info.Length < _maxSizeBytes)
        {
            return;
        }

        var index = 1;
        string rotatedPath;
        do
        {
            rotatedPath = Path.Combine(
                info.DirectoryName ?? _logDirectory!,
                $"{Path.GetFileNameWithoutExtension(path)}.{index}{LogExt}");
            index++;
        } while (File.Exists(rotatedPath));

        File.Move(path, rotatedPath);
    }

    private static void MaybePruneOldLogs(DateTimeOffset now)
    {
        _writesSincePrune++;
        if (_writesSincePrune < PruneEveryWrites && now < _nextPruneAt)
        {
            return;
        }

        PruneOldLogs(now);
        _writesSincePrune = 0;
        _nextPruneAt = now.Add(PruneInterval);
    }

    private static void PruneOldLogs(DateTimeOffset now)
    {
        var cutoff = now.UtcDateTime.AddDays(-_retentionDays);
        foreach (var file in Directory.GetFiles(_logDirectory!, $"{LogBaseName}-*{LogExt}"))
        {
            try
            {
                var lastWriteUtc = File.GetLastWriteTimeUtc(file);
                if (lastWriteUtc < cutoff)
                {
                    File.Delete(file);
                }
            }
            catch
            {
                // Best-effort cleanup.
            }
        }
    }

    private static IReadOnlyList<string> TailLines(string filePath, int maxLines)
    {
        if (maxLines <= 0 || !File.Exists(filePath))
        {
            return Array.Empty<string>();
        }

        var queue = new Queue<string>(maxLines);
        using (var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
        using (var reader = new StreamReader(stream, Encoding.UTF8, true))
        {
            while (!reader.EndOfStream)
            {
                var line = reader.ReadLine();
                if (line == null)
                {
                    continue;
                }

                if (queue.Count == maxLines)
                {
                    queue.Dequeue();
                }

                queue.Enqueue(line);
            }
        }

        return queue.ToArray();
    }
}
