using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SubZ.Plugin.Configuration;

namespace SubZ.Plugin.Services;

public sealed class TranslationExecutionEngine
{
    private readonly TranslationProfileResolver _profileResolver = new TranslationProfileResolver();
    private readonly SubtitleTagProtector _protector = new SubtitleTagProtector();
    private readonly ISubtitleTranslationClient _client = new OpenAICompatibleTranslationClient();

    public async Task<TranslationTargetResult> ProcessTargetAsync(string target, PluginOptions options, CancellationToken cancellationToken)
    {
        var files = VideoFileResolver.ResolveVideoFiles(target).ToList();
        if (files.Count == 0)
        {
            throw new InvalidOperationException($"No video files found for target: {target}");
        }

        var scanDirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var skipped = 0;
        foreach (var videoFile in files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (await ProcessVideoAsync(videoFile, options, cancellationToken, scanDirs).ConfigureAwait(false))
            {
                skipped++;
            }
        }

        FlushRescanNotifications(scanDirs);
        return skipped == files.Count ? TranslationTargetResult.Skipped : TranslationTargetResult.Completed;
    }

    private async Task<bool> ProcessVideoAsync(string videoFile, PluginOptions options, CancellationToken cancellationToken, HashSet<string> scanDirs)
    {
        var targetCode = options.GetTargetLanguageCode();
        var debugEnabled = options.EnableDebugLog;
        InMemoryTranslationJobDispatcher.AppendRuntimeLog("Info", $"Start video: {videoFile}");

        if (VideoFileResolver.HasTargetSubtitle(videoFile, targetCode))
        {
            InMemoryTranslationJobDispatcher.AppendRuntimeLog("Info", $"Skip existing target subtitle: {videoFile} -> {targetCode}");
            return true;
        }

        if (SubtitleSourceResolver.HasEmbeddedTargetSubtitle(videoFile, options, debugEnabled))
        {
            InMemoryTranslationJobDispatcher.AppendRuntimeLog("Info", $"Skip existing embedded target subtitle: {videoFile} -> {targetCode}");
            return true;
        }

        var source = SubtitleSourceResolver.Resolve(videoFile, options, debugEnabled);
        if (source.IsEmbedded)
        {
            InMemoryTranslationJobDispatcher.AppendRuntimeLog("Info", $"Using embedded subtitle: {videoFile}");
        }
        else if (!string.IsNullOrWhiteSpace(source.SourceSubtitlePath))
        {
            InMemoryTranslationJobDispatcher.AppendRuntimeLog("Info", $"Using external subtitle: {source.SourceSubtitlePath}");
        }

        try
        {
            var sourceSubtitle = source.SourceSubtitlePath;
            if (string.IsNullOrWhiteSpace(sourceSubtitle) || !File.Exists(sourceSubtitle))
            {
                InMemoryTranslationJobDispatcher.AppendRuntimeLog("Error", $"No subtitle source found: {videoFile}");
                throw new InvalidOperationException($"No usable subtitle source found for: {videoFile}");
            }

            var validSourceSubtitle = sourceSubtitle!;
            var cues = SubtitleIO.ReadFromFile(validSourceSubtitle);
            if (cues.Count == 0)
            {
                InMemoryTranslationJobDispatcher.AppendRuntimeLog("Error", $"Subtitle has no cues: {validSourceSubtitle}");
                throw new InvalidOperationException($"Subtitle source has no cues: {validSourceSubtitle}");
            }
            InMemoryTranslationJobDispatcher.AppendRuntimeLog("Info", $"Loaded cues: {cues.Count} | Source: {validSourceSubtitle}");

            var profile = _profileResolver.ResolveActiveProfile(options);
            InMemoryTranslationJobDispatcher.AppendRuntimeLog("Info", $"Translating with provider={profile.Provider}, model={profile.Model}");
            var translator = new BatchSelfHealingTranslator(_client, _protector, _profileResolver);
            var rawLines = cues.Select(static c => c.Text).ToList();

            var translated = await translator.TranslateAsync(rawLines, options, cancellationToken, cues).ConfigureAwait(false);
            var bilingualCues = BilingualSubtitleComposer.BuildBilingualCues(cues, translated.OutputLines);

            var outputPath = BilingualSubtitleComposer.BuildOutputPath(videoFile, targetCode, options.OutputFormat);
            if (string.Equals(options.OutputFormat, "srt", StringComparison.OrdinalIgnoreCase))
            {
                SubtitleIO.WriteSrtBilingual(outputPath, bilingualCues);
            }
            else
            {
                SubtitleIO.WriteAssBilingual(
                    outputPath,
                    bilingualCues,
                    options.AssFontName,
                    options.AssFontSize,
                    options.AssPrimaryColor);
            }
            InMemoryTranslationJobDispatcher.AppendRuntimeLog("Info", $"Output written: {outputPath}");

            CollectScanDirectory(videoFile, scanDirs);

            Plugin.LogInfo(
                "SubZ translation usage | File={0} | PromptTokens={1} | CompletionTokens={2} | TotalTokens={3} | CueCount={4}",
                videoFile,
                translated.PromptTokens,
                translated.CompletionTokens,
                translated.TotalTokens,
                cues.Count);
            InMemoryTranslationJobDispatcher.AppendRuntimeLog(
                "Info",
                $"Token usage | File={videoFile} | Prompt={translated.PromptTokens}, Completion={translated.CompletionTokens}, Total={translated.TotalTokens}, Cues={cues.Count}");

            return false;
        }
        finally
        {
            SubtitleSourceResolver.CleanupTemp(source);
        }
    }

    private static void CollectScanDirectory(string videoFile, HashSet<string> scanDirs)
    {
        var dir = Path.GetDirectoryName(videoFile);
        if (!string.IsNullOrWhiteSpace(dir))
        {
            scanDirs.Add(dir);
        }
    }

    private static void FlushRescanNotifications(HashSet<string> scanDirs)
    {
        var monitor = Plugin.LibraryMonitor;
        if (monitor == null) return;
        if (scanDirs.Count == 0) return;

        foreach (var dir in scanDirs)
        {
            try
            {
                monitor.ReportFileSystemChanged(dir);
                InMemoryTranslationJobDispatcher.AppendRuntimeLog("Info", $"Submitted folder for Emby rescan: {dir}");
            }
            catch (Exception ex)
            {
                InMemoryTranslationJobDispatcher.AppendRuntimeLog("Warn", $"Emby rescan failed for {dir}: {ex.Message}");
            }
        }
    }
}
