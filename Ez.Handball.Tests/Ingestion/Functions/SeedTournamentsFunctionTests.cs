using Ez.Handball.Ingestion.Functions;
using Ez.Handball.Ingestion.Services;
using Ez.Handball.Shared.Entities;
using Moq;
using Xunit;

namespace Ez.Handball.Tests.Ingestion.Functions;

public class SeedTournamentsFunctionTests
{
    private readonly Mock<ITableWriter> _tableWriter = new();

    public SeedTournamentsFunctionTests()
    {
        _tableWriter
            .Setup(t => t.QueryAsync<SeasonEntity>("Seasons", "PartitionKey eq 'season'", default))
            .ReturnsAsync(new List<SeasonEntity>());
    }

    private SeedTournamentsFunction CreateSut() => new(_tableWriter.Object);

    [Theory]
    [InlineData("1",       10)]
    [InlineData("1-final", 20)]
    [InlineData("2",       30)]
    [InlineData("2-final", 40)]
    [InlineData("cup",     50)]
    public void TournamentDefinitions_AssignExpectedPriorityForDivision(string division, int expectedPriority)
    {
        var matching = SeedTournamentsFunction.TournamentDefinitions
            .Where(d => d.Division == division)
            .ToList();

        Assert.NotEmpty(matching);
        Assert.All(matching, d => Assert.Equal(expectedPriority, d.Priority));
    }

    [Fact]
    public async Task ProcessAsync_SeedsTournamentsWithSeasonLabelPartitionKey()
    {
        var (season, seeded) = await CreateSut().ProcessAsync("2025");

        Assert.Equal("2025-26", season);
        Assert.Equal(SeedTournamentsFunction.TournamentDefinitions.Count, seeded);

        // Every tournament row is written under the label partition.
        _tableWriter.Verify(t => t.UpsertAsync(
            "Tournaments",
            It.Is<TournamentEntity>(e => e.PartitionKey == "2025-26"),
            default), Times.Exactly(SeedTournamentsFunction.TournamentDefinitions.Count));

        // A specific known tournament is seeded correctly.
        _tableWriter.Verify(t => t.UpsertAsync(
            "Tournaments",
            It.Is<TournamentEntity>(e =>
                e.PartitionKey == "2025-26" &&
                e.RowKey == "8444" &&
                e.Name == "Olís deild karla"),
            default), Times.Once);
    }

    [Fact]
    public async Task ProcessAsync_NullSeasonParam_FallsBackToCurrentYearLabel()
    {
        var expected = Ez.Handball.Shared.SeasonLabel.Format(DateTime.UtcNow.Year);

        var (season, _) = await CreateSut().ProcessAsync(null);

        Assert.Equal(expected, season);
        _tableWriter.Verify(t => t.UpsertAsync(
            "Tournaments",
            It.Is<TournamentEntity>(e => e.PartitionKey == expected),
            default), Times.Exactly(SeedTournamentsFunction.TournamentDefinitions.Count));
    }

    [Fact]
    public async Task ProcessAsync_UpsertsSeasonRow_AsCurrent_WhenTableEmpty()
    {
        await CreateSut().ProcessAsync("2025");

        _tableWriter.Verify(t => t.UpsertAsync(
            "Seasons",
            It.Is<SeasonEntity>(e =>
                e.PartitionKey == "season" &&
                e.RowKey == "2025-26" &&
                e.StartYear == 2025 &&
                e.IsCurrent == true),
            default,
            Azure.Data.Tables.TableUpdateMode.Replace), Times.Once);
    }

    [Fact]
    public async Task ProcessAsync_OlderSeason_DoesNotBecomeCurrent_AndLeavesNewestAlone()
    {
        _tableWriter
            .Setup(t => t.QueryAsync<SeasonEntity>("Seasons", "PartitionKey eq 'season'", default))
            .ReturnsAsync(new List<SeasonEntity>
            {
                new() { PartitionKey = "season", RowKey = "2025-26", StartYear = 2025, IsCurrent = true }
            });

        await CreateSut().ProcessAsync("2023");

        _tableWriter.Verify(t => t.UpsertAsync(
            "Seasons",
            It.Is<SeasonEntity>(e => e.RowKey == "2023-24" && e.IsCurrent == false),
            default,
            Azure.Data.Tables.TableUpdateMode.Replace), Times.Once);

        _tableWriter.Verify(t => t.UpsertAsync(
            "Seasons",
            It.Is<SeasonEntity>(e => e.RowKey == "2025-26"),
            default,
            Azure.Data.Tables.TableUpdateMode.Replace), Times.Never);
    }

    [Fact]
    public async Task ProcessAsync_NewerSeason_BecomesCurrent_AndClearsPreviousCurrent()
    {
        _tableWriter
            .Setup(t => t.QueryAsync<SeasonEntity>("Seasons", "PartitionKey eq 'season'", default))
            .ReturnsAsync(new List<SeasonEntity>
            {
                new() { PartitionKey = "season", RowKey = "2024-25", StartYear = 2024, IsCurrent = true }
            });

        await CreateSut().ProcessAsync("2025");

        _tableWriter.Verify(t => t.UpsertAsync(
            "Seasons",
            It.Is<SeasonEntity>(e => e.RowKey == "2025-26" && e.IsCurrent == true),
            default,
            Azure.Data.Tables.TableUpdateMode.Replace), Times.Once);

        _tableWriter.Verify(t => t.UpsertAsync(
            "Seasons",
            It.Is<SeasonEntity>(e => e.RowKey == "2024-25" && e.IsCurrent == false),
            default,
            Azure.Data.Tables.TableUpdateMode.Replace), Times.Once);
    }
}
