using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace SubZ.Plugin.Services;

public static class BilingualSubtitleComposer
{
    public static List<SubtitleCue> BuildBilingualCues(
        IReadOnlyList<SubtitleCue> source,
        IReadOnlyList<string> translated,
        bool translationOnly = false)
    {
        var cues = new List<SubtitleCue>(source.Count);
        for (var i = 0; i < source.Count; i++)
        {
            var dst = NormalizeSpaces(i < translated.Count ? translated[i] : source[i].Text);

            string merged;
            if (translationOnly)
            {
                merged = dst;
            }
            else
            {
                var src = NormalizeSpaces(source[i].Text);
                merged = ReflowToTwoLines(src, dst);
            }

            cues.Add(new SubtitleCue
            {
                Index = i + 1,
                Start = source[i].Start,
                End = source[i].End,
                Text = merged
            });
        }

        return cues;
    }

    public static string BuildOutputPath(string videoFile, string targetCode, string outputFormat)
    {
        var dir = System.IO.Path.GetDirectoryName(videoFile) ?? string.Empty;
        var stem = System.IO.Path.GetFileNameWithoutExtension(videoFile);
        var ext = string.Equals(outputFormat, "srt", StringComparison.OrdinalIgnoreCase) ? ".srt" : ".ass";
        return System.IO.Path.Combine(dir, $"{stem}.subz.{targetCode}{ext}");
    }

    private static string NormalizeSpaces(string input)
    {
        var s = (input ?? string.Empty).Replace("\r", "").Trim();
        s = Regex.Replace(s, "\\n+", "\\n");
        return s;
    }

    private static string ReflowToTwoLines(string original, string translated)
    {
        var line1 = original.Replace("\n", " ").Trim();
        var line2 = translated.Replace("\n", " ").Trim();

        if (string.IsNullOrWhiteSpace(line1))
        {
            return line2;
        }

        if (string.IsNullOrWhiteSpace(line2))
        {
            return line1;
        }

        return line1 + "\n" + line2;
    }
}
