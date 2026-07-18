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
}
