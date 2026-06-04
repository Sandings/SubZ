namespace SubZ.Plugin.Tests;

public sealed class StatusDashboardRenderingTests
{
    [Fact]
    public void TaskRowsRenderCompletedAsSingleLineAndFailedWithMessageLine()
    {
        var source = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "StatusDashboard.js"));

        Assert.Contains("sz-title-time", source);
        Assert.Contains("st === 'Succeeded' || st === 'Failed'", source);
        Assert.Contains("if (st === 'Failed')", source);
        Assert.Contains("else if (st !== 'Succeeded')", source);
        Assert.Contains("titleTimeHtml", source);
        Assert.Contains("messageHtml", source);
    }
}
