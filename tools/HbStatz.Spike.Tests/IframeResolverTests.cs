using HbStatz.Spike;
using Xunit;

namespace HbStatz.Spike.Tests;

public class IframeResolverTests
{
    private static string Fixture(string name) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "fixtures", name));

    [Fact]
    public void Outfield_HasInlineTable_AndNoIframesNeeded()
    {
        var html = Fixture("outfield.html");
        Assert.True(IframeResolver.HasInlineStatsTable(html));
    }

    [Fact]
    public void GkWrapper_HasNoInlineTable_AndResolvesAbsoluteIframeSources()
    {
        var html = Fixture("gk-wrapper.html");
        Assert.False(IframeResolver.HasInlineStatsTable(html));

        var sources = IframeResolver.ExtractSources(html, "https://hbstatz.is/OlisDeildKarlaTolfraedi2.php");
        Assert.Contains(sources, s => s.EndsWith("test22.php"));
        Assert.All(sources, s => Assert.StartsWith("https://", s));
    }
}
