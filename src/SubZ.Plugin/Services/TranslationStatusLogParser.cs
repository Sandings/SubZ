using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace SubZ.Plugin.Services;

public static class TranslationStatusLogParser
{
    private const string RemovedSkippedPrefix = "Removed skipped target from task status: ";
    private static readonly IReadOnlyList<StatusPrefix> Prefixes = new[]
    {
        new StatusPrefix("Queued target: ", TranslationTaskState.Queued, UiText.Queued()),
        new StatusPrefix("Running: ", TranslationTaskState.Running, UiText.Running()),
        new StatusPrefix("Completed: ", TranslationTaskState.Succeeded, UiText.Completed()),
        new StatusPrefix("Failed: ", TranslationTaskState.Failed, string.Empty),
        new StatusPrefix("Stopped: ", TranslationTaskState.Stopped, UiText.StoppedByUser()),
        new StatusPrefix("Paused and re-queued: ", TranslationTaskState.Queued, UiText.Paused())
    };

    public static IReadOnlyList<TranslationTaskStatus> Parse(
        IEnumerable<string> lines,
        DateTimeOffset cutoffUtc)
    {
        var statuses = new Dictionary<string, TranslationTaskStatus>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in lines ?? Array.Empty<string>())
        {
            var removedTarget = ParseRemovedSkippedTarget(line, cutoffUtc);
            if (!string.IsNullOrWhiteSpace(removedTarget))
            {
                statuses.Remove(removedTarget!);
                continue;
            }

            var parsed = ParseLine(line, cutoffUtc);
            if (parsed == null)
            {
                continue;
            }

            statuses[parsed.Target] = parsed;
        }

        return statuses.Values
            .OrderByDescending(static s => s.UpdatedAt)
            .ToArray();
    }

    private static string? ParseRemovedSkippedTarget(string? line, DateTimeOffset cutoffUtc)
    {
        var message = TryParseMessage(line, cutoffUtc);
        if (string.IsNullOrWhiteSpace(message))
        {
            return null;
        }

        var value = message!;
        if (!value.StartsWith(RemovedSkippedPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var target = value.Substring(RemovedSkippedPrefix.Length).Trim();
        return string.IsNullOrWhiteSpace(target) ? null : target;
    }

    private static TranslationTaskStatus? ParseLine(string? line, DateTimeOffset cutoffUtc)
    {
        var message = TryParseMessage(line, cutoffUtc);
        if (string.IsNullOrWhiteSpace(message))
        {
            return null;
        }

        var value = message!;
        var timestamp = ParseTimestamp(line, cutoffUtc);
        if (timestamp == null)
        {
            return null;
        }

        foreach (var prefix in Prefixes)
        {
            if (!value.StartsWith(prefix.Text, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var targetAndMessage = value.Substring(prefix.Text.Length).Trim();
            if (string.IsNullOrWhiteSpace(targetAndMessage))
            {
                return null;
            }

            var target = targetAndMessage;
            var statusMessage = prefix.Message;
            if (prefix.State == TranslationTaskState.Failed)
            {
                var separator = targetAndMessage.IndexOf(" | ", StringComparison.Ordinal);
                if (separator >= 0)
                {
                    target = targetAndMessage.Substring(0, separator).Trim();
                    statusMessage = targetAndMessage.Substring(separator + 3).Trim();
                }
                else
                {
                    statusMessage = "Failed";
                }
            }

            if (string.IsNullOrWhiteSpace(target))
            {
                return null;
            }

            return new TranslationTaskStatus
            {
                Target = target,
                State = prefix.State,
                Message = statusMessage,
                UpdatedAt = timestamp.Value
            };
        }

        return null;
    }

    private static string? TryParseMessage(string? line, DateTimeOffset cutoffUtc)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            return null;
        }

        var value = line ?? string.Empty;
        var firstSpace = value.IndexOf(' ');
        if (firstSpace <= 0)
        {
            return null;
        }

        if (ParseTimestamp(value, cutoffUtc) == null)
        {
            return null;
        }

        var messageStart = value.IndexOf("] ", firstSpace, StringComparison.Ordinal);
        if (messageStart < 0)
        {
            return null;
        }

        return value.Substring(messageStart + 2);
    }

    private static DateTimeOffset? ParseTimestamp(string? line, DateTimeOffset cutoffUtc)
    {
        var value = line ?? string.Empty;
        var firstSpace = value.IndexOf(' ');
        if (firstSpace <= 0)
        {
            return null;
        }

        var timestampText = value.Substring(0, firstSpace);
        if (!DateTimeOffset.TryParse(timestampText, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var timestamp))
        {
            return null;
        }

        timestamp = timestamp.ToUniversalTime();
        return timestamp < cutoffUtc.ToUniversalTime() ? null : timestamp;
    }

    private sealed class StatusPrefix
    {
        public StatusPrefix(string text, TranslationTaskState state, string message)
        {
            Text = text;
            State = state;
            Message = message;
        }

        public string Text { get; }
        public TranslationTaskState State { get; }
        public string Message { get; }
    }
}
