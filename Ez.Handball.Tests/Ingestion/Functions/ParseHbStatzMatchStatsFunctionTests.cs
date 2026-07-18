using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Azure.Data.Tables;
using Ez.Handball.Ingestion.Functions;
using Ez.Handball.Ingestion.Services;
using Ez.Handball.Shared.Entities;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Ez.Handball.Tests.Ingestion.Functions;

public class ParseHbStatzMatchStatsFunctionTests
{
    private readonly Mock<ITableWriter> _tableWriter = new();
    private readonly Mock<FunctionContext> _context = new();

    public ParseHbStatzMatchStatsFunctionTests()
    {
        _context.SetupGet(c => c.CancellationToken).Returns(CancellationToken.None);
    }

    private ParseHbStatzMatchStatsFunction CreateSut() =>
        new(_tableWriter.Object, NullLogger<ParseHbStatzMatchStatsFunction>.Instance);

    [Fact]
    public async Task RunAsync_MatchNotFound_DoesNotReconcileOrUpsert()
    {
        _tableWriter
            .Setup(t => t.QueryAsync<MatchEntity>("Matches", "RowKey eq '12922'", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<MatchEntity>());

        await CreateSut().RunAsync("<html></html>", "12922", "home", _context.Object);

        _tableWriter.Verify(t => t.QueryAsync<PlayerEntity>(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        _tableWriter.Verify(t => t.UpsertAsync(It.IsAny<string>(), It.IsAny<PlayerStatEntity>(), It.IsAny<CancellationToken>(), It.IsAny<TableUpdateMode>()), Times.Never);
    }

    [Fact]
    public async Task RunAsync_HappyPath_ParsesHTMLAndUpsertsMergedStats()
    {
        var match = new MatchEntity
        {
            PartitionKey = "8444",
            RowKey = "12922",
            HomeTeamId = "385-karlar",
            AwayTeamId = "390-karlar"
        };

        _tableWriter
            .Setup(t => t.QueryAsync<MatchEntity>("Matches", "RowKey eq '12922'", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<MatchEntity> { match });

        var roster = new List<PlayerEntity>
        {
            new()
            {
                PartitionKey = "385-karlar",
                RowKey = "player-1",
                Name = "Ólafur Stefánsson",
                JerseyNumber = "10"
            }
        };

        _tableWriter
            .Setup(t => t.QueryAsync<PlayerEntity>("Players", "PartitionKey eq '385-karlar'", It.IsAny<CancellationToken>()))
            .ReturnsAsync(roster);

        var htmlContent = @"
            <table>
                <thead>
                    <tr>
                        <th>Nafn</th>
                        <th>Mörk</th>
                        <th>xG</th>
                        <th>Sköpuð færi (Stoð)</th>
                    </tr>
                </thead>
                <tbody>
                    <tr>
                        <td>10. Ólafur Stefánsson</td>
                        <td>5</td>
                        <td>4.5</td>
                        <td>2</td>
                    </tr>
                </tbody>
            </table>";

        await CreateSut().RunAsync(htmlContent, "12922", "home", _context.Object);

        _tableWriter.Verify(t => t.UpsertAsync("PlayerStats",
            It.Is<PlayerStatEntity>(ps =>
                ps.PartitionKey == "12922" &&
                ps.RowKey == "player-1" &&
                ps.Assists == 2 &&
                ps.ExpectedGoals == 4.5),
            It.IsAny<CancellationToken>(),
            TableUpdateMode.Merge), Times.Once);
    }
}
