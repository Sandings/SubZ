using System.Collections.Generic;
using System.ComponentModel;
using Emby.Web.GenericEdit;
using Emby.Web.GenericEdit.Common;
using MediaBrowser.Model.Attributes;

namespace SubZ.Plugin.Configuration;

public sealed class PluginOptions : EditableOptionsBase
{
    public override string EditorTitle => "SubZ";

    [DisplayName("启用插件 / Enable Plugin")]
    public bool Enabled { get; set; } = true;

    [DisplayName("仅手动目标模式 / Manual Target Only Mode")]
    [Description("开启后跳过自动入库触发，仅允许手动目标执行。 / When enabled, skip library-ingest auto trigger and run manual targets only.")]
    public bool ManualTargetOnlyMode { get; set; } = false;

    [DisplayName("目标语言 / Target Language")]
    [Description("翻译目标语言。默认中文（简体）。 / Target subtitle language. Default is Chinese (Simplified).")]
    public SupportedLanguageOption TargetLanguageOption { get; set; } = SupportedLanguageOption.ZhHans;

    [Browsable(false)]
    public string TargetLanguage { get; set; } = "zh-CN";

    [DisplayName("手动执行目标文件夹 / Manual Target Folder")]
    [Description("手动执行时选择目标文件夹。 / Select a folder for manual execution.")]
    [EditFolderPicker]
    public string ManualRunTargetFolder { get; set; } = string.Empty;

    [DisplayName("手动执行目标文件 / Manual Target File")]
    [Description("手动执行时选择目标文件（可选）。 / Select a file for manual execution (optional).")]
    [EditFilePicker]
    public string ManualRunTargetFile { get; set; } = string.Empty;

    [DisplayName("立即执行一次 / Run Once Now")]
    [Description("勾选后点击保存，将按上方目标执行一次，随后自动复位。 / Run one task on save and auto-reset.")]
    public bool ManualRunNow { get; set; } = false;

    [DisplayName("输出格式 / Output Format")]
    public OutputFormatOption OutputFormatOption { get; set; } = OutputFormatOption.Srt;

    [Browsable(false)]
    public string OutputFormat { get; set; } = "srt";

    [DisplayName("ASS 字体名称 / ASS Font Name")]
    public string AssFontName { get; set; } = "微软雅黑";

    [DisplayName("ASS 字体大小 / ASS Font Size")]
    public int AssFontSize { get; set; } = 60;

    [DisplayName("ASS 字体颜色 / ASS Font Color")]
    [Description("仅在输出格式为 ASS 时生效。支持 &H00RRGGBB 或 #RRGGBB。 / Effective only for ASS output.")]
    public string AssPrimaryColor { get; set; } = "&H00FFFFFF";

    [DisplayName("API 提供方 / API Provider")]
    public string ApiProvider { get; set; } = "deepseek";

    [DisplayName("API 地址 / API Base URL")]
    public string ApiBaseUrl { get; set; } = "https://api.deepseek.com";

    [DisplayName("API 密钥 / API Key")]
    public string ApiKey { get; set; } = string.Empty;

    [DisplayName("模型 / Model")]
    public string Model { get; set; } = "deepseek-v4-flash";

    [DisplayName("每批字幕条数 / Batch Size")]
    [Description("单次请求发给模型的字幕数量。值越大通常越快，但超时和错位风险更高。 / Number of subtitle cues per request.")]
    public int BatchSize { get; set; } = 120;

    [DisplayName("源语言偏好 / Preferred Source Language")]
    [Description("字幕源语言偏好，用于外挂字幕匹配和内嵌字幕轨优先选择。 / Preferred source subtitle language for external and embedded track selection.")]
    public SourceLanguageOption PreferredSourceLanguageOption { get; set; } = SourceLanguageOption.En;

    [Browsable(false)]
    public string PreferredSourceLanguage { get; set; } = "en";

    [DisplayName("FFmpeg 路径 / FFmpeg Path")]
    [Description("用于抽取封装字幕轨的 ffmpeg 可执行文件路径。留空则使用 Emby 内置路径。 / Path to ffmpeg executable for embedded subtitle extraction. Leave empty to use Emby's built-in path.")]
    public string FfmpegPath { get; set; } = string.Empty;

    [DisplayName("FFprobe 路径 / FFprobe Path")]
    [Description("用于探测封装字幕轨信息的 ffprobe 可执行文件路径。留空则使用 Emby 内置路径。 / Path to ffprobe executable for subtitle stream probing. Leave empty to use Emby's built-in path.")]
    public string FfprobePath { get; set; } = string.Empty;

    [DisplayName("日志文件最大大小(MB) / Log File Max Size (MB)")]
    [Description("单个日志文件的最大大小（MB）。超过后自动滚动。 / Maximum size per log file in MB before auto-rotation.")]
    public int LogFileMaxSizeMb { get; set; } = 10;

    [DisplayName("日志保留天数 / Log Retention Days")]
    [Description("超过该天数的旧日志会自动清理。 / Log files older than this number of days are deleted automatically.")]
    public int LogRetentionDays { get; set; } = 7;

    [DisplayName("详细日志模式(Debug) / Debug Log Mode")]
    [Description("开启后记录完整请求/响应正文与调试信息（可能包含字幕文本，日志体积会显著增加）。 / When enabled, logs full request/response bodies and debug details.")]
    public bool EnableDebugLog { get; set; } = false;

    [DisplayName("状态页访问路径 / Status Page URL")]
    [Description("外部状态页地址。可选中后复制。推荐配合外部状态页查看实时进度与控制。 / External status page URL.")]
    public string StatusPageUrl { get; set; } = string.Empty;

    [Browsable(false)]
    public List<TranslationApiProfile> Profiles { get; set; } = new List<TranslationApiProfile>
    {
        new TranslationApiProfile
        {
            Name = "default",
            Provider = "deepseek",
            BaseUrl = "https://api.deepseek.com",
            ApiKey = string.Empty,
            Model = "deepseek-v4-flash",
            TimeoutSeconds = 90,
            Temperature = 0.1,
            BatchSize = 120,
            ParallelRequests = 2,
            RetryCount = 2
        }
    };

    [DisplayName("保留字幕标签 / Preserve Subtitle Tags")]
    public bool PreserveSubtitleTags { get; set; } = true;

    [DisplayName("启用尾段重试 / Enable Tail Retry")]
    public bool EnableTailRetry { get; set; } = true;

    [DisplayName("尾段重试次数 / Tail Retry Attempts")]
    public int TailRetryMaxAttempts { get; set; } = 2;

    [DisplayName("优先非强制字幕轨 / Prefer Non-Forced Track")]
    public bool PreferNonForcedTrack { get; set; } = true;

    [DisplayName("优先非听障字幕轨 / Prefer Non-HI Track")]
    public bool PreferNonHearingImpairedTrack { get; set; } = true;

    [DisplayName("优先文本字幕轨 / Prefer Text Subtitle Track")]
    public bool PreferTextSubtitleTrack { get; set; } = true;

    [Browsable(false)]
    public IEnumerable<EditorSelectOption> LibrarySelectItemsList { get; set; }
        = new List<EditorSelectOption>();

    [DisplayName("翻译的媒体库 / Libraries to Translate")]
    [Description("仅翻译勾选的媒体库。空表示翻译所有媒体库。 / Only translate media in selected libraries. Leave empty to translate all libraries.")]
    [EditMultilSelect]
    [SelectItemsSource(nameof(LibrarySelectItemsList))]
    public string SelectedLibraryIds { get; set; } = string.Empty;

    public string GetTargetLanguageCode()
    {
        return LanguageCodeMap.ToCode(TargetLanguageOption);
    }
}
