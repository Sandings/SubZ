namespace SubZ.Plugin.Services;

public sealed class TranslationTargetResult
{
    public static readonly TranslationTargetResult Completed = new TranslationTargetResult(false);
    public static readonly TranslationTargetResult Skipped = new TranslationTargetResult(true);

    private TranslationTargetResult(bool skipped)
    {
        SkippedAll = skipped;
    }

    public bool SkippedAll { get; }
}
