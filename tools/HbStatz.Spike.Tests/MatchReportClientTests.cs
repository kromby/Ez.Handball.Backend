using HbStatz.Spike;
using Xunit;

namespace HbStatz.Spike.Tests;

public class MatchReportClientTests
{
    [Fact]
    public void TeamPageUrl_HomeUsesTest6b_AwayUsesTest7b()
    {
        Assert.Equal("https://hbstatz.is/test6b.php?ID=12922", MatchReportClient.TeamPageUrl("12922", "home"));
        Assert.Equal("https://hbstatz.is/test7b.php?ID=12922", MatchReportClient.TeamPageUrl("12922", "away"));
    }
}
