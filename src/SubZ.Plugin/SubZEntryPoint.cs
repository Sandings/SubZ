using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Plugins;
using SubZ.Plugin.Api;
using SubZ.Plugin.Configuration;
using SubZ.Plugin.Services;

namespace SubZ.Plugin;

public sealed class SubZEntryPoint : IServerEntryPoint
{
    private readonly ILibraryManager _libraryManager;
    private readonly ConcurrentDictionary<string, DateTime> _recentlyProcessedFolders = new(StringComparer.OrdinalIgnoreCase);
    private static readonly TimeSpan DebounceWindow = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan StartupGracePeriod = TimeSpan.FromSeconds(30);
    private readonly DateTime _startupBlockedUntil;
    private bool _disposed;

    public SubZEntryPoint(ILibraryManager libraryManager)
    {
        _libraryManager = libraryManager;
        _startupBlockedUntil = DateTime.UtcNow + StartupGracePeriod;
    }

    public void Run()
    {
        _libraryManager.ItemAdded += OnItemAdded;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _libraryManager.ItemAdded -= OnItemAdded;
    }

    private void OnItemAdded(object? sender, ItemChangeEventArgs e)
    {
        if (_disposed) return;
        if (DateTime.UtcNow < _startupBlockedUntil) return;

        var plugin = Plugin.Instance;
        if (plugin is null) return;

        var options = plugin.CurrentOptions;
        if (!options.Enabled || options.ManualTargetOnlyMode) return;

        // Only process Movies and Episodes; Season is indirectly covered via Episode's Series path
        if (e.Item is not Movie && e.Item is not Episode) return;

        // Check if the item is within the selected libraries
        if (!IsLibrarySelected(e.Item, options)) return;

        var folderPath = ResolveFolderPath(e.Item);
        if (string.IsNullOrWhiteSpace(folderPath)) return;

        if (!TryAcquireFolderLock(folderPath!)) return;

        var orchestrator = new TranslationOrchestrator(
            options,
            new TranslationExecutionPolicy(options),
            ManualTranslationService.Dispatcher,
            new TranslationProfileResolver());

        orchestrator.TryRunForLibraryIngestAsync(
            new[] { folderPath! },
            CancellationToken.None).ContinueWith(
            static t =>
            {
                if (!t.IsFaulted || t.Exception is null) return;
                var ex = t.Exception.GetBaseException() ?? t.Exception;
                Plugin.LogErrorException("Library ingest hook failed.", ex);
                InMemoryTranslationJobDispatcher.AppendRuntimeLog("Error",
                    $"Library ingest hook failed: {ex.Message}");
            },
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    // Checks whether the item belongs to a user-selected library.
    // If SelectedLibraryIds is empty, all libraries are considered selected.
    private bool IsLibrarySelected(BaseItem item, PluginOptions options)
    {
        var selectedIds = options.SelectedLibraryIds;
        if (string.IsNullOrWhiteSpace(selectedIds))
        {
            // No libraries selected => default to processing all libraries
            return true;
        }

        var selectedSet = new HashSet<string>(
            selectedIds.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(s => s.Trim()),
            StringComparer.OrdinalIgnoreCase);

        if (selectedSet.Count == 0)
        {
            return true;
        }

        try
        {
            var itemPath = item.ContainingFolderPath;
            if (string.IsNullOrWhiteSpace(itemPath))
            {
                return false;
            }

            var virtualFolders = _libraryManager.GetVirtualFolders();
            foreach (var folder in virtualFolders)
            {
                var folderId = folder.ItemId ?? folder.Id;
                if (string.IsNullOrWhiteSpace(folderId)) continue;

                if (!selectedSet.Contains(folderId)) continue;

                // Match the library's physical location paths
                if (folder.Locations != null &&
                    folder.Locations.Any(loc => !string.IsNullOrWhiteSpace(loc) &&
                        itemPath.StartsWith(loc, StringComparison.OrdinalIgnoreCase)))
                {
                    return true;
                }
            }
        }
        catch (Exception ex)
        {
            Plugin.LogErrorException("Failed to check library selection for item.", ex);
            // Fail open on error to avoid blocking the normal library ingestion flow
            return true;
        }

        return false;
    }

    private static string? ResolveFolderPath(BaseItem item)
    {
        if (item is Episode)
            return item.ContainingFolderPath;

        if (item is Movie)
            return item.ContainingFolderPath;

        return null;
    }

    private bool TryAcquireFolderLock(string folderPath)
    {
        var now = DateTime.UtcNow;
        var accepted = false;

        _recentlyProcessedFolders.AddOrUpdate(
            folderPath,
            _ => { accepted = true; return now; },
            (_, existing) =>
            {
                if (now - existing >= DebounceWindow)
                {
                    accepted = true;
                    return now;
                }

                accepted = false;
                return existing;
            });

        if (!accepted) return false;

        // 惰性清理过期条目
        var cutoff = now - TimeSpan.FromHours(1);
        foreach (var key in _recentlyProcessedFolders.Keys)
        {
            if (_recentlyProcessedFolders.TryGetValue(key, out var ts) && ts < cutoff)
                _recentlyProcessedFolders.TryRemove(key, out _);
        }

        return true;
    }
}
