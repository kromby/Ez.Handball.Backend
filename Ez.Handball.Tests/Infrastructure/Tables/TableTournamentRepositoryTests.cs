using Ez.Handball.Infrastructure.TableAccess;
using Ez.Handball.Shared.Entities;
using Moq;

namespace Ez.Handball.Tests.Infrastructure.Tables;

public class TableTournamentRepositoryTests
{
    private readonly Mock<ITableQuery> _query = new();

    private TableTournamentRepository CreateSut() => new(_query.Object);

    private void SetupTournaments(string season, params TournamentEntity[] rows) =>
        _query.Setup(q => q.QueryAsync<TournamentEntity>(
                Ez.Handball.Infrastructure.Tables.Tournaments,
                $"PartitionKey eq '{ODataFilter.Escape(season)}' and Enabled eq true",
                default))
              .Returns(ToAsync(rows));

    private static TournamentEntity Tournament(
        string id, string name, string gender, int priority, string season = "2025-26") =>
        new()
        {
            PartitionKey = season,
            RowKey = id,
            Name = name,
            Gender = gender,
            Division = "1",
            Enabled = true,
            Priority = priority
        };

    private static async IAsyncEnumerable<T> ToAsync<T>(IEnumerable<T> items)
    {
        foreach (var i in items) yield return i;
        await Task.CompletedTask;
    }

    [Fact]
    public async Task ListEnabledBySeasonAsync_FiltersByPartitionAndEnabled()
    {
        SetupTournaments("2025-26", Tournament("8444", "Olís deild karla", "karlar", 10));

        var result = await CreateSut().ListEnabledBySeasonAsync("2025-26", default);

        var t = Assert.Single(result);
        Assert.Equal("8444", t.TournamentId);
        Assert.Equal("Olís deild karla", t.Name);
        Assert.Equal("karlar", t.Gender);

        _query.Verify(q => q.QueryAsync<TournamentEntity>(
            Ez.Handball.Infrastructure.Tables.Tournaments,
            "PartitionKey eq '2025-26' and Enabled eq true",
            default), Times.Once);
    }

    [Fact]
    public async Task ListEnabledBySeasonAsync_OrdersByPriorityThenIcelandicName()
    {
        SetupTournaments("2025-26",
            Tournament("3", "Þór",   "karlar", 30),
            Tournament("1", "Valur", "karlar", 30),
            Tournament("2", "Akur",  "karlar", 10));

        var result = await CreateSut().ListEnabledBySeasonAsync("2025-26", default);

        Assert.Equal(new[] { "Akur", "Valur", "Þór" }, result.Select(t => t.Name).ToArray());
    }

    [Fact]
    public async Task ListEnabledBySeasonAsync_EscapesSeasonInFilter()
    {
        SetupTournaments("o'brien");

        await CreateSut().ListEnabledBySeasonAsync("o'brien", default);

        _query.Verify(q => q.QueryAsync<TournamentEntity>(
            Ez.Handball.Infrastructure.Tables.Tournaments,
            "PartitionKey eq 'o''brien' and Enabled eq true",
            default), Times.Once);
    }

    [Fact]
    public async Task ListEnabledBySeasonAsync_NoRows_ReturnsEmpty()
    {
        SetupTournaments("2025-26");

        Assert.Empty(await CreateSut().ListEnabledBySeasonAsync("2025-26", default));
    }
}
