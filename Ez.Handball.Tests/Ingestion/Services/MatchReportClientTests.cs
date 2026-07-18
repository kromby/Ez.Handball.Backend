using Ez.Handball.Ingestion.Services;
using Moq;
using Xunit;

namespace Ez.Handball.Tests.Ingestion.Services;

public class MatchReportClientTests
{
    [Fact]
    public async Task GetTeamPageHtmlAsync_CallsExpectedUrls()
    {
        var mockClient = new Mock<IHbStatzClient>();
        mockClient.Setup(c => c.GetHtmlAsync(It.IsAny<string>(), default))
                  .ReturnsAsync("<html></html>");

        var sut = new MatchReportClient(mockClient.Object);
        await sut.GetTeamPageHtmlAsync("12922", "home");
        await sut.GetTeamPageHtmlAsync("12922", "away");

        mockClient.Verify(c => c.GetHtmlAsync("https://hbstatz.is/test6b.php?ID=12922", default), Times.Once);
        mockClient.Verify(c => c.GetHtmlAsync("https://hbstatz.is/test7b.php?ID=12922", default), Times.Once);
    }

    [Theory]
    [InlineData("HOME", "https://hbstatz.is/test6b.php?ID=12922")]
    [InlineData("Away", "https://hbstatz.is/test7b.php?ID=12922")]
    public async Task GetTeamPageHtmlAsync_CaseInsensitiveSide_CallsExpectedUrls(string side, string expectedUrl)
    {
        var mockClient = new Mock<IHbStatzClient>();
        mockClient.Setup(c => c.GetHtmlAsync(It.IsAny<string>(), default))
                  .ReturnsAsync("<html></html>");

        var sut = new MatchReportClient(mockClient.Object);
        await sut.GetTeamPageHtmlAsync("12922", side);

        mockClient.Verify(c => c.GetHtmlAsync(expectedUrl, default), Times.Once);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task GetTeamPageHtmlAsync_InvalidMatchId_ThrowsArgumentException(string? invalidMatchId)
    {
        var mockClient = new Mock<IHbStatzClient>();
        var sut = new MatchReportClient(mockClient.Object);

        await Assert.ThrowsAsync<ArgumentException>(() => sut.GetTeamPageHtmlAsync(invalidMatchId!, "home"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("foo")]
    public async Task GetTeamPageHtmlAsync_InvalidSide_ThrowsArgumentException(string? invalidSide)
    {
        var mockClient = new Mock<IHbStatzClient>();
        var sut = new MatchReportClient(mockClient.Object);

        await Assert.ThrowsAsync<ArgumentException>(() => sut.GetTeamPageHtmlAsync("12922", invalidSide!));
    }
}
