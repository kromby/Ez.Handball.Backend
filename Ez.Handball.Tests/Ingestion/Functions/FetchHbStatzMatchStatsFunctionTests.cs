using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Ez.Handball.Ingestion.Functions;
using Ez.Handball.Ingestion.Services;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Ez.Handball.Tests.Ingestion.Functions;

public class FetchHbStatzMatchStatsFunctionTests
{
    private readonly Mock<IMatchReportClient> _reportClient = new();
    private readonly Mock<IBlobArchiver> _blobArchiver = new();
    private readonly Mock<FunctionContext> _context = new();

    public FetchHbStatzMatchStatsFunctionTests()
    {
        _context.SetupGet(c => c.CancellationToken).Returns(CancellationToken.None);

        var serviceProvider = new Mock<System.IServiceProvider>();
        serviceProvider.Setup(s => s.GetService(typeof(ILogger<FetchHbStatzMatchStatsFunction>)))
            .Returns(NullLogger<FetchHbStatzMatchStatsFunction>.Instance);
        _context.SetupGet(c => c.InstanceServices).Returns(serviceProvider.Object);
    }

    private FetchHbStatzMatchStatsFunction CreateSut() =>
        new(_reportClient.Object, _blobArchiver.Object);

    [Fact]
    public async Task RunAsync_ScrapesAndArchivesHtmlForBothTeams()
    {
        var matchId = "12922";
        var messageJson = JsonSerializer.Serialize(new { MatchId = matchId });

        _reportClient.Setup(c => c.GetTeamPageHtmlAsync(matchId, "home", It.IsAny<CancellationToken>()))
            .ReturnsAsync("<html>home</html>");
        _reportClient.Setup(c => c.GetTeamPageHtmlAsync(matchId, "away", It.IsAny<CancellationToken>()))
            .ReturnsAsync("<html>away</html>");

        await CreateSut().RunAsync(messageJson, _context.Object);

        _blobArchiver.Verify(b => b.SaveAsync($"hbstatz/matches/{matchId}/players-home.html", "<html>home</html>", It.IsAny<CancellationToken>()), Times.Once);
        _blobArchiver.Verify(b => b.SaveAsync($"hbstatz/matches/{matchId}/players-away.html", "<html>away</html>", It.IsAny<CancellationToken>()), Times.Once);
    }
}
