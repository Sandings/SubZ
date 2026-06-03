using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Emby.Web.GenericEdit.Common;
using MediaBrowser.Common;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Plugins;
using MediaBrowser.Model.Drawing;
using MediaBrowser.Model.Logging;
using MediaBrowser.Model.Plugins;
using SubZ.Plugin.Api;
using SubZ.Plugin.Configuration;
using SubZ.Plugin.Services;

namespace SubZ.Plugin;

public sealed class Plugin : BasePluginSimpleUI<PluginOptions>, IHasThumbImage, IHasWebPages
{
    private const string PreferredLinuxLogBaseDir = "/config/plugins/SubZ.Plugin";
    public static Plugin? Instance { get; private set; }
    public static ILibraryMonitor? LibraryMonitor { get; private set; }
    private static ILogger? _logger;
    private readonly IFfmpegToolPathProvider _ffmpegToolPathProvider;
    private readonly ILibraryManager? _libraryManager;

    public Plugin(IApplicationHost applicationHost, ILibraryManager libraryManager)
        : base(applicationHost)
    {
        Instance = this;
        _ffmpegToolPathProvider = new ApplicationHostFfmpegToolPathProvider(applicationHost);
        _libraryManager = libraryManager;
        TryInitLogger(applicationHost);
        TryInitLibraryMonitor(applicationHost);
        var options = CurrentOptions;
        ConfigureRuntimeFileLogger(options);
        InMemoryTranslationJobDispatcher.AppendRuntimeLog("Info", "SubZ plugin started.");
        ScheduleMediaToolPathValidation();
    }

    public override string Name => "SubZ";

    public override string Description => "Translate official subtitles into selected language via LLM API.";

    public override Guid Id => Guid.Parse("e14380f8-5dd0-47be-84e3-b3700a42bf85");

    public PluginOptions CurrentOptions => GetOptions();

    public ImageFormat ThumbImageFormat => ImageFormat.Png;

    public Stream GetThumbImage()
    {
        var assembly = typeof(Plugin).GetTypeInfo().Assembly;
        var stream = assembly.GetManifestResourceStream("SubZ.Assets.Logo.subz-logo-thumb.png");
        if (stream == null)
        {
            throw new InvalidOperationException("Embedded logo resource not found: SubZ.Assets.Logo.subz-logo-thumb.png");
        }

        return stream;
    }

    public IEnumerable<PluginPageInfo> GetPages()
    {
        var ns = GetType().Namespace;
        return new[]
        {
            new PluginPageInfo
            {
                Name = "SubZStatusV3",
                EmbeddedResourcePath = ns + ".UI.StatusDashboardV2.html",
                DisplayName = "SubZ Status",
                EnableInMainMenu = true,
                MenuIcon = "subtitles"
            },
            new PluginPageInfo
            {
                Name = "SubZStatus",
                EmbeddedResourcePath = ns + ".UI.StatusDashboardV2.html"
            },
            new PluginPageInfo
            {
                Name = "SubZStatusJsV2",
                EmbeddedResourcePath = ns + ".UI.StatusDashboard.js"
            },
            new PluginPageInfo
            {
                Name = "SubZStatusJs",
                EmbeddedResourcePath = ns + ".UI.StatusDashboard.js"
            },
            new PluginPageInfo
            {
                // Backward-compatible alias for browsers that cached the old page controller name.
                Name = "StatusDashboardJs",
                EmbeddedResourcePath = ns + ".UI.StatusDashboard.js"
            }
        };
    }

    public static void LogInfo(string message, params object[] args)
    {
        _logger?.Info(message, args);
    }

    public static void LogWarn(string message, params object[] args)
    {
        _logger?.Warn(message, args);
    }

    public static void LogErrorException(string message, Exception ex, params object[] args)
    {
        _logger?.ErrorException(message, ex, args);
    }

    protected override PluginOptions OnBeforeShowUI(PluginOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.StatusPageUrl))
        {
            options.StatusPageUrl = "http://localhost:18123/subz-status.html";
        }

        if (!string.IsNullOrWhiteSpace(options.TargetLanguage))
        {
            options.TargetLanguageOption = LanguageCodeMap.FromCode(options.TargetLanguage);
        }
        else
        {
            options.TargetLanguageOption = SupportedLanguageOption.ZhHans;
            options.TargetLanguage = LanguageCodeMap.ToCode(options.TargetLanguageOption);
        }

        if (string.IsNullOrWhiteSpace(options.OutputFormat))
        {
            options.OutputFormat = "srt";
        }

        options.OutputFormatOption = OutputFormatMap.FromCode(options.OutputFormat);

        if (string.IsNullOrWhiteSpace(options.ApiProvider))
        {
            options.ApiProvider = "deepseek";
        }

        if (string.IsNullOrWhiteSpace(options.ApiBaseUrl))
        {
            options.ApiBaseUrl = "https://api.deepseek.com";
        }

        if (string.IsNullOrWhiteSpace(options.Model))
        {
            options.Model = "deepseek-v4-flash";
        }

        if (string.IsNullOrWhiteSpace(options.PreferredSourceLanguage))
        {
            options.PreferredSourceLanguage = "en";
        }
        options.PreferredSourceLanguageOption = SourceLanguageMap.FromCode(options.PreferredSourceLanguage);

        if (options.LogFileMaxSizeMb <= 0)
        {
            options.LogFileMaxSizeMb = 10;
        }

        if (options.LogRetentionDays <= 0)
        {
            options.LogRetentionDays = 7;
        }

        if (options.Profiles != null && options.Profiles.Count > 0)
        {
            var activeProfile = options.Profiles[0];
            options.ApiProvider = string.IsNullOrWhiteSpace(activeProfile.Provider)
                ? options.ApiProvider
                : activeProfile.Provider;
            options.ApiBaseUrl = string.IsNullOrWhiteSpace(activeProfile.BaseUrl)
                ? options.ApiBaseUrl
                : activeProfile.BaseUrl;
            options.ApiKey = string.IsNullOrWhiteSpace(activeProfile.ApiKey)
                ? options.ApiKey
                : activeProfile.ApiKey;
            options.Model = string.IsNullOrWhiteSpace(activeProfile.Model)
                ? options.Model
                : activeProfile.Model;
            options.BatchSize = activeProfile.BatchSize > 0
                ? activeProfile.BatchSize
                : options.BatchSize;
        }

        // Populate library multi-select list
        PopulateLibrarySelectItems(options);

        return options;
    }

    private void PopulateLibrarySelectItems(PluginOptions options)
    {
        try
        {
            if (_libraryManager == null)
            {
                LogWarn("ILibraryManager not available, cannot populate library list.");
                return;
            }

            var virtualFolders = _libraryManager.GetVirtualFolders();
            var items = virtualFolders
                .Where(f => !string.IsNullOrWhiteSpace(f.Name) && !string.IsNullOrWhiteSpace(f.ItemId))
                .Select(f => new EditorSelectOption
                {
                    Name = f.Name,
                    Value = f.ItemId
                })
                .OrderBy(o => o.Name)
                .ToList();

            options.LibrarySelectItemsList = items;
        }
        catch (Exception ex)
        {
            LogErrorException("Failed to populate library select items.", ex);
        }
    }

    protected override bool OnOptionsSaving(PluginOptions options)
    {
        options.TargetLanguage = options.GetTargetLanguageCode();
        options.OutputFormat = OutputFormatMap.ToCode(options.OutputFormatOption);

        if (options.Profiles == null || options.Profiles.Count == 0)
        {
            options.Profiles = new System.Collections.Generic.List<TranslationApiProfile>
            {
                new TranslationApiProfile()
            };
        }

        var profile = options.Profiles[0];
        profile.Name = "default";
        profile.Provider = string.IsNullOrWhiteSpace(options.ApiProvider) ? "deepseek" : options.ApiProvider;
        profile.BaseUrl = string.IsNullOrWhiteSpace(options.ApiBaseUrl) ? "https://api.deepseek.com" : options.ApiBaseUrl;
        profile.ApiKey = options.ApiKey ?? string.Empty;
        profile.Model = string.IsNullOrWhiteSpace(options.Model) ? "deepseek-v4-flash" : options.Model;
        if (profile.Temperature <= 0 || profile.Temperature > 2)
        {
            profile.Temperature = 0.1;
        }
        profile.BatchSize = options.BatchSize <= 0 ? 120 : options.BatchSize;
        options.ApiProvider = profile.Provider;
        options.ApiBaseUrl = profile.BaseUrl;
        options.ApiKey = profile.ApiKey;
        options.Model = profile.Model;
        options.BatchSize = profile.BatchSize;
        options.PreferredSourceLanguage = SourceLanguageMap.ToCode(options.PreferredSourceLanguageOption);
        options.FfmpegPath = (options.FfmpegPath ?? string.Empty).Trim();
        options.FfprobePath = (options.FfprobePath ?? string.Empty).Trim();

        options.LogFileMaxSizeMb = options.LogFileMaxSizeMb <= 0 ? 10 : options.LogFileMaxSizeMb;
        options.LogRetentionDays = options.LogRetentionDays <= 0 ? 7 : options.LogRetentionDays;

        ConfigureRuntimeFileLogger(options);
        ValidateMediaToolPaths(options);

        if (options.ManualRunNow)
        {
            var targetFolder = (options.ManualRunTargetFolder ?? string.Empty).Trim();
            var targetFile = (options.ManualRunTargetFile ?? string.Empty).Trim();

            if (targetFolder.Length > 0 && targetFile.Length > 0)
            {
                throw new InvalidOperationException("Please specify either Manual Run Target Folder or Manual Run Target File, not both.");
            }

            if (targetFolder.Length == 0 && targetFile.Length == 0)
            {
                throw new InvalidOperationException("Please specify Manual Run Target Folder or Manual Run Target File before enabling Run Manual Translation Now.");
            }

            var request = new RunManualTranslation
            {
                TargetFolderPath = targetFolder.Length > 0 ? targetFolder : null,
                TargetFilePath = targetFile.Length > 0 ? targetFile : null
            };

            options.ManualRunNow = false;
            QueueManualTranslationFromSave(request);
        }

        return base.OnOptionsSaving(options);
    }

    private void ScheduleMediaToolPathValidation()
    {
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(3)).ConfigureAwait(false);
                ValidateMediaToolPaths(CurrentOptions);
            }
            catch (Exception ex)
            {
                InMemoryTranslationJobDispatcher.AppendRuntimeLog("Warn", $"Delayed media tool path validation failed: {ex.Message}");
                LogWarn("Delayed media tool path validation failed: {0}", ex.Message);
            }
        });
    }

    private static void TryInitLogger(IApplicationHost applicationHost)
    {
        try
        {
            var logManager = applicationHost.Resolve<ILogManager>();
            _logger = logManager?.GetLogger("SubZ");
        }
        catch
        {
            _logger = null;
        }
    }

    private static void TryInitLibraryMonitor(IApplicationHost applicationHost)
    {
        try
        {
            LibraryMonitor = applicationHost.Resolve<ILibraryMonitor>();
        }
        catch
        {
            LibraryMonitor = null;
        }
    }

    private void ConfigureRuntimeFileLogger(PluginOptions options)
    {
        try
        {
            var baseDir = PreferredLinuxLogBaseDir;
            if (!TryPrepareDirectory(baseDir))
            {
                baseDir = DataFolderPath;
                if (string.IsNullOrWhiteSpace(baseDir) || !TryPrepareDirectory(baseDir))
                {
                    baseDir = Path.Combine(AppContext.BaseDirectory, "subz");
                    Directory.CreateDirectory(baseDir);
                }
            }
            FileRuntimeLogger.Configure(baseDir, options.LogFileMaxSizeMb, options.LogRetentionDays);
        }
        catch (Exception ex)
        {
            LogWarn("Failed to configure runtime file logger: {0}", ex.Message);
        }
    }

    private static bool TryPrepareDirectory(string? directory)
    {
        if (string.IsNullOrWhiteSpace(directory))
        {
            return false;
        }

        try
        {
            Directory.CreateDirectory(directory);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static void QueueManualTranslationFromSave(RunManualTranslation request)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                var service = new ManualTranslationService();
                var response = await service.Post(request).ConfigureAwait(false);
                if (!response.Accepted)
                {
                    InMemoryTranslationJobDispatcher.AppendRuntimeLog("Error", $"Manual run request rejected: {response.Message}");
                    LogWarn("Manual run request rejected: {0}", response.Message);
                    return;
                }

                InMemoryTranslationJobDispatcher.AppendRuntimeLog("Info", $"Manual run request accepted from configuration save: {response.PlannedTargets.Count} target(s).");
            }
            catch (Exception ex)
            {
                InMemoryTranslationJobDispatcher.AppendRuntimeLog("Error", $"Manual run request failed from configuration save: {ex.Message}");
                LogErrorException("Manual run request failed from configuration save.", ex);
            }
        });
    }

    private static void ValidateMediaToolPaths(PluginOptions options)
    {
        var paths = ResolveFfmpegToolPaths(options);
        ValidateToolPath("ffmpeg", paths.FfmpegPath, paths.FfmpegSource);
        ValidateToolPath("ffprobe", paths.FfprobePath, paths.FfprobeSource);
    }

    public static FfmpegToolPaths ResolveFfmpegToolPaths(PluginOptions options)
    {
        return FfmpegToolPathResolver.Resolve(options, Instance?._ffmpegToolPathProvider);
    }

    private static void ValidateToolPath(string toolName, string? configuredPath, string source)
    {
        var path = (configuredPath ?? string.Empty).Trim();
        if (path.Length == 0)
        {
            var msg = $"{toolName} path is empty. Please configure a valid executable path.";
            LogWarn(msg);
            InMemoryTranslationJobDispatcher.AppendRuntimeLog("Warn", msg);
            return;
        }

        if (!File.Exists(path))
        {
            var msg = $"{toolName} path not found ({source}): {path}";
            LogWarn(msg);
            InMemoryTranslationJobDispatcher.AppendRuntimeLog("Warn", msg);
            return;
        }

        InMemoryTranslationJobDispatcher.AppendRuntimeLog("Info", $"{toolName} path OK ({source}): {path}");
    }
}
