using System;
using System.Collections.Generic;
using System.Linq;
using SubZ.Plugin.Configuration;

namespace SubZ.Plugin.Services;

public sealed class SubtitleTrackInfo
{
    public int Id { get; set; }
    public string Language { get; set; } = string.Empty;
    public string Codec { get; set; } = string.Empty;
    public bool IsForced { get; set; }
    public bool IsHearingImpaired { get; set; }
    public bool IsDefault { get; set; }
    public bool IsTextTrack { get; set; }
}

public sealed class SubtitleTrackSelector
{
    private static readonly HashSet<string> TextSubtitleCodecs = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "subrip", "srt", "ass", "ssa", "webvtt", "mov_text"
    };

    public SubtitleTrackInfo? SelectBest(IReadOnlyList<SubtitleTrackInfo> tracks, PluginOptions options)
    {
        if (tracks is null || tracks.Count == 0)
        {
            return null;
        }

        return tracks
            .OrderByDescending(t => Score(t, options))
            .ThenBy(t => t.Id)
            .FirstOrDefault();
    }

    public static bool HasTargetTrack(IReadOnlyList<SubtitleTrackInfo> tracks, PluginOptions options)
    {
        if (tracks is null || tracks.Count == 0)
        {
            return false;
        }

        var targetCode = NormalizeLanguageCode(options.GetTargetLanguageCode(), "zh-CN");
        return tracks.Any(t => IsLanguageMatch(t.Language, targetCode));
    }

    private static int Score(SubtitleTrackInfo track, PluginOptions options)
    {
        var score = 0;
        var preferredSource = NormalizeLanguageCode(options.PreferredSourceLanguage, "en");
        var preferredShort = preferredSource.Split('-')[0];
        var trackLanguage = (track.Language ?? string.Empty).Trim();

        if (options.PreferTextSubtitleTrack)
        {
            score += track.IsTextTrack ? 500 : -500;
        }

        if (options.PreferNonForcedTrack)
        {
            score += track.IsForced ? -120 : 120;
        }

        if (options.PreferNonHearingImpairedTrack)
        {
            score += track.IsHearingImpaired ? -80 : 80;
        }

        if (track.IsDefault)
        {
            score += 40;
        }

        if (!string.IsNullOrWhiteSpace(trackLanguage))
        {
            if (string.Equals(trackLanguage, preferredSource, StringComparison.OrdinalIgnoreCase))
            {
                score += 140;
            }
            else if (!string.IsNullOrWhiteSpace(preferredShort) &&
                string.Equals(trackLanguage, preferredShort, StringComparison.OrdinalIgnoreCase))
            {
                score += 100;
            }
            else if (!string.IsNullOrWhiteSpace(preferredShort) &&
                trackLanguage.StartsWith(preferredShort, StringComparison.OrdinalIgnoreCase))
            {
                score += 80;
            }
        }

        if (string.Equals(track.Language, options.GetTargetLanguageCode(), StringComparison.OrdinalIgnoreCase))
        {
            // Lightly demote already-target-language tracks for translation scenarios.
            score -= 20;
        }

        var codec = track.Codec?.Trim().ToLowerInvariant() ?? string.Empty;
        if (TextSubtitleCodecs.Contains(codec))
        {
            score += 30;
        }

        if (codec == "pgs" || codec == "dvd_subtitle" || codec == "vobsub")
        {
            score -= 200;
        }

        return score;
    }

    private static bool IsLanguageMatch(string? language, string targetCode)
    {
        var normalizedLanguage = NormalizeLanguageToken(language);
        var normalizedTarget = NormalizeLanguageToken(targetCode);
        if (string.IsNullOrWhiteSpace(normalizedLanguage) || string.IsNullOrWhiteSpace(normalizedTarget))
        {
            return false;
        }

        if (string.Equals(normalizedLanguage, normalizedTarget, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (normalizedTarget.StartsWith("zh", StringComparison.OrdinalIgnoreCase))
        {
            if (normalizedTarget == "zh-tw" || normalizedTarget == "zh-hant" || normalizedTarget == "zh-hk")
            {
                return normalizedLanguage == "zh"
                    || normalizedLanguage == "zh-tw"
                    || normalizedLanguage == "zh-hant"
                    || normalizedLanguage == "zh-hk"
                    || normalizedLanguage == "chi"
                    || normalizedLanguage == "zho"
                    || normalizedLanguage == "cmn";
            }

            return normalizedLanguage == "zh"
                || normalizedLanguage == "zh-cn"
                || normalizedLanguage == "zh-hans"
                || normalizedLanguage == "zh-sg"
                || normalizedLanguage == "chi"
                || normalizedLanguage == "zho"
                || normalizedLanguage == "cmn";
        }

        var targetShort = normalizedTarget.Split('-')[0];
        return string.Equals(normalizedLanguage, targetShort, StringComparison.OrdinalIgnoreCase)
            || normalizedLanguage.StartsWith(targetShort + "-", StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeLanguageToken(string? value)
    {
        return (value ?? string.Empty).Trim().Replace('_', '-').ToLowerInvariant();
    }

    private static string NormalizeLanguageCode(string? configured, string fallback)
    {
        var raw = (configured ?? string.Empty).Trim();
        return string.IsNullOrWhiteSpace(raw) ? fallback : raw;
    }
}
