using Ez.Handball.Domain;
using Ez.Handball.Infrastructure.TableAccess;
using Ez.Handball.Shared.Entities;
using Moq;

namespace Ez.Handball.Tests.Infrastructure.Tables;

public class TableTournamentRepositoryTests
{
    private readonly Mock<ITableQuery> _query = new();

    private TableTournamentRepository CreateSut() => new(_query.Object);

    private void SetupActive(string season, params TournamentEntity[] rows) =>
        _query.Setup(q => q.QueryAsync<TournamentEntity>(
                Ez.Handball.Infrastructure.Tables.Tournaments,
                $"PartitionKey eq '{ODataFilter.Escape(season)}' and Active eq true",
                default))
              .Returns(ToAsync(rows));

    private void SetupAll(string season, params TournamentEntity[] rows) =>
        _query.Setup(q => q.QueryAsync<TournamentEntity>(
                Ez.Handball.Infrastructure.Tables.Tournaments,
                $"PartitionKey eq '{ODataFilter.Escape(season)}'",
                default))
              .Returns(ToAsync(rows));

    private static TournamentEntity Tournament(
        string id, string name, string gender, int priority,
        string type = "league", string competitionId = "olis-karla",
        string competitionName = "Olís deild karla", bool active = true,
        string season = "2025-26") =>
        new()
        {
            PartitionKey = season,
            RowKey = id,
            Name = name,
            Gender = gender,
            Division = "1",
            Type = type,
            CompetitionId = competitionId,
            CompetitionName = competitionName,
            Ingest = true,
            Active = active,
            Priority = priority
        };

    private static async IAsyncEnumerable<T> ToAsync<T>(IEnumerable<T> items)
    {
        foreach (var i in items) yield return i;
        await Task.CompletedTask;
    }

    [Fact]
    public async Task ListActiveBySeasonAsync_FiltersByPartitionAndActive_AndMapsNewFields()
    {
        SetupActive("2025-26", Tournament("8444", "Olís deild karla", "karlar", 10));

        var result = await CreateSut().ListActiveBySeasonAsync("2025-26", default);

        var t = Assert.Single(result);
        Assert.Equal("8444", t.TournamentId);
        Assert.Equal("Olís deild karla", t.Name);
        Assert.Equal("karlar", t.Gender);
        Assert.Equal(TournamentType.League, t.Type);
        Assert.Equal("olis-karla", t.CompetitionId);
        Assert.Equal("Olís deild karla", t.CompetitionName);

        _query.Verify(q => q.QueryAsync<TournamentEntity>(
            Ez.Handball.Infrastructure.Tables.Tournaments,
            "PartitionKey eq '2025-26' and Active eq true",
            default), Times.Once);
    }

    [Fact]
    public async Task ListActiveBySeasonAsync_OrdersByPriorityThenIcelandicName()
    {
        SetupActive("2025-26",
            Tournament("3", "Þór",   "karlar", 30),
            Tournament("1", "Valur", "karlar", 30),
            Tournament("2", "Akur",  "karlar", 10));

        var result = await CreateSut().ListActiveBySeasonAsync("2025-26", default);

        Assert.Equal(new[] { "Akur", "Valur", "Þór" }, result.Select(t => t.Name).ToArray());
    }

    [Fact]
    public async Task ListActiveBySeasonAsync_EscapesSeasonInFilter()
    {
        SetupActive("o'brien");

        await CreateSut().ListActiveBySeasonAsync("o'brien", default);

        _query.Verify(q => q.QueryAsync<TournamentEntity>(
            Ez.Handball.Infrastructure.Tables.Tournaments,
            "PartitionKey eq 'o''brien' and Active eq true",
            default), Times.Once);
    }

    [Fact]
    public async Task ListActiveBySeasonAsync_NoRows_ReturnsEmpty()
    {
        SetupActive("2025-26");

        Assert.Empty(await CreateSut().ListActiveBySeasonAsync("2025-26", default));
    }

    [Fact]
    public async Task ListBySeasonAsync_FiltersByPartitionOnly_IncludingInactiveRows()
    {
        SetupAll("2025-26",
            Tournament("8444", "Olís deild karla", "karlar", 10, type: "league", active: true),
            Tournament("8427", "Olís deild úrslit karla", "karlar", 20,
                type: "playoffs", active: false));

        var result = await CreateSut().ListBySeasonAsync("2025-26", default);

        Assert.Equal(2, result.Count);
        Assert.Contains(result, t => t.TournamentId == "8427" && t.Type == TournamentType.Playoffs);

        _query.Verify(q => q.QueryAsync<TournamentEntity>(
            Ez.Handball.Infrastructure.Tables.Tournaments,
            "PartitionKey eq '2025-26'",
            default), Times.Once);
    }
}
