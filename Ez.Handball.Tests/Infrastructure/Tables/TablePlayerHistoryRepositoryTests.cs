using Ez.Handball.Application.Abstractions;
using Ez.Handball.Domain;
using Ez.Handball.Infrastructure;
using Ez.Handball.Infrastructure.TableAccess;
using Ez.Handball.Shared.Entities;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Ez.Handball.Tests.Infrastructure.Tables;

public class TablePlayerHistoryRepositoryTests
{
    private readonly Mock<ITableQuery> _query = new();

    private IPlayerHistoryRepository CreateSut() =>
        new TablePlayerHistoryRepository(_query.Object, NullLogger<TablePlayerHistoryRepository>.Instance);

    private void SetupStats(string playerId, params PlayerStatEntity[] rows) =>
        _query.Setup(q => q.QueryAsync<PlayerStatEntity>(
                Ez.Handball.Infrastructure.Tables.PlayerStats, $"RowKey eq '{playerId}'", default))
              .Returns(ToAsync(rows));

    private void SetupTournaments(string season, params TournamentEntity[] rows) =>
        _query.Setup(q => q.QueryAsync<TournamentEntity>(
                Ez.Handball.Infrastructure.Tables.Tournaments, $"PartitionKey eq '{season}'", default))
              .Returns(ToAsync(rows));

    private static PlayerStatEntity Stat(
        string matchId, string playerId, string season, string tournamentId,
        string teamId, string? clubName, int g, int y, int tm, int r) =>
        new()
        {
            PartitionKey = matchId, RowKey = playerId,
            Goals = g, YellowCards = y, TwoMinuteSuspensions = tm, RedCards = r,
            TournamentId = tournamentId, Season = season,
            TeamId = teamId, ClubName = clubName
        };

    private static TournamentEntity Tour(string season, string id, string name, int priority) =>
        new()
        {
            PartitionKey = season, RowKey = id,
            Name = name, Gender = "karlar", Division = "1", Ingest = true, Priority = priority
        };

    private static async IAsyncEnumerable<T> ToAsync<T>(IEnumerable<T> items)
    {
        foreach (var i in items) yield return i;
        await Task.CompletedTask;
    }

    [Fact]
    public async Task GetByPlayerAsync_NoStats_ReturnsEmptyHistory_AndNullTotals()
    {
        SetupStats("nope");

        var result = await CreateSut().GetByPlayerAsync("nope", default);

        Assert.Empty(result.Entries);
        Assert.Null(result.Totals);
        _query.Verify(q => q.QueryAsync<TournamentEntity>(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task GetByPlayerAsync_OneMatch_OneEntry_TotalsMirrorEntry()
    {
        SetupStats("12345", Stat("m1", "12345", "2025-26", "8444", "385-karlar", "Stjarnan", 5, 0, 1, 0));
        SetupTournaments("2025-26", Tour("2025-26", "8444", "Olís deild karla", 10));

        var result = await CreateSut().GetByPlayerAsync("12345", default);

        var entry = Assert.Single(result.Entries);
        Assert.Equal("2025-26", entry.Season);
        Assert.Equal("8444", entry.TournamentId);
        Assert.Equal("Olís deild karla", entry.TournamentName);
        Assert.Equal("385", entry.ClubId);
        Assert.Equal("Stjarnan", entry.ClubName);
        Assert.Equal(1, entry.Games);
        Assert.Equal(5, entry.TotalGoals);
        Assert.Equal(5.0, entry.AvgGoals);

        Assert.NotNull(result.Totals);
        Assert.Equal(1, result.Totals!.Games);
        Assert.Equal(5, result.Totals.TotalGoals);
        Assert.Equal(5.0, result.Totals.AvgGoals);
    }

    [Fact]
    public async Task GetByPlayerAsync_ThreeMatchesSameGroup_SumsAndAverages()
    {
        SetupStats("12345",
            Stat("m1", "12345", "2025-26", "8444", "385-karlar", "Stjarnan", 5, 0, 1, 0),
            Stat("m2", "12345", "2025-26", "8444", "385-karlar", "Stjarnan", 6, 1, 0, 0),
            Stat("m3", "12345", "2025-26", "8444", "385-karlar", "Stjarnan", 7, 0, 2, 0));
        SetupTournaments("2025-26", Tour("2025-26", "8444", "Olís deild karla", 10));

        var result = await CreateSut().GetByPlayerAsync("12345", default);

        var entry = Assert.Single(result.Entries);
        Assert.Equal(3, entry.Games);
        Assert.Equal(18, entry.TotalGoals);
        Assert.Equal(1, entry.TotalYellowCards);
        Assert.Equal(3, entry.TotalTwoMinuteSuspensions);
        Assert.Equal(6.0, entry.AvgGoals);
        Assert.Equal(1.0 / 3, entry.AvgYellowCards);
    }

    [Fact]
    public async Task GetByPlayerAsync_TwoTournamentsSameSeason_TwoEntries()
    {
        SetupStats("12345",
            Stat("m1", "12345", "2025-26", "8444", "385-karlar", "Stjarnan", 5, 0, 0, 0),
            Stat("m2", "12345", "2025-26", "8437", "385-karlar", "Stjarnan", 3, 0, 0, 0));
        SetupTournaments("2025-26",
            Tour("2025-26", "8444", "Olís deild karla",   10),
            Tour("2025-26", "8437", "Powerade bikar karla", 50));

        var result = await CreateSut().GetByPlayerAsync("12345", default);

        Assert.Equal(2, result.Entries.Count);
        Assert.Equal(8, result.Totals!.TotalGoals);
    }

    [Fact]
    public async Task GetByPlayerAsync_MidSeasonTransfer_TwoEntriesDistinctClubs()
    {
        SetupStats("12345",
            Stat("m1", "12345", "2025-26", "8444", "385-karlar", "Stjarnan", 5, 0, 0, 0),
            Stat("m2", "12345", "2025-26", "8444", "410-karlar", "Valur",    3, 0, 0, 0));
        SetupTournaments("2025-26", Tour("2025-26", "8444", "Olís deild karla", 10));

        var result = await CreateSut().GetByPlayerAsync("12345", default);

        Assert.Equal(2, result.Entries.Count);
        Assert.Contains(result.Entries, e => e.ClubId == "385" && e.ClubName == "Stjarnan");
        Assert.Contains(result.Entries, e => e.ClubId == "410" && e.ClubName == "Valur");
    }

    [Fact]
    public async Task GetByPlayerAsync_MultipleSeasons_TournamentsQueriedOncePerSeason()
    {
        SetupStats("12345",
            Stat("m1", "12345", "2025-26", "8444", "385-karlar", "Stjarnan", 5, 0, 0, 0),
            Stat("m2", "12345", "2024-25", "8444", "385-karlar", "Stjarnan", 4, 0, 0, 0));
        SetupTournaments("2025-26", Tour("2025-26", "8444", "Olís 25", 10));
        SetupTournaments("2024-25", Tour("2024-25", "8444", "Olís 24", 10));

        await CreateSut().GetByPlayerAsync("12345", default);

        _query.Verify(q => q.QueryAsync<TournamentEntity>(
            Ez.Handball.Infrastructure.Tables.Tournaments, "PartitionKey eq '2025-26'", default), Times.Once);
        _query.Verify(q => q.QueryAsync<TournamentEntity>(
            Ez.Handball.Infrastructure.Tables.Tournaments, "PartitionKey eq '2024-25'", default), Times.Once);
    }

    [Fact]
    public async Task GetByPlayerAsync_MissingTournament_NameNull_PriorityMaxValue_SortsLast()
    {
        SetupStats("12345",
            Stat("m1", "12345", "2025-26", "8444", "385-karlar", "Stjarnan", 5, 0, 0, 0),
            Stat("m2", "12345", "2025-26", "9999", "385-karlar", "Stjarnan", 3, 0, 0, 0));   // missing
        SetupTournaments("2025-26", Tour("2025-26", "8444", "Olís deild karla", 10));   // no 9999

        var result = await CreateSut().GetByPlayerAsync("12345", default);

        Assert.Equal(2, result.Entries.Count);
        Assert.Equal("8444", result.Entries[0].TournamentId);  // known one first
        Assert.Equal("9999", result.Entries[1].TournamentId);  // unknown last
        Assert.Null(result.Entries[1].TournamentName);
    }

    [Fact]
    public async Task GetByPlayerAsync_SortOrder_SeasonDesc_PriorityAsc_ClubAsc()
    {
        SetupStats("12345",
            Stat("m1", "12345", "2025-26", "8444", "385-karlar", "Stjarnan", 1, 0, 0, 0),   // 2025-26, prio 10, Stjarnan
            Stat("m2", "12345", "2025-26", "8437", "385-karlar", "Stjarnan", 1, 0, 0, 0),   // 2025-26, prio 50, Stjarnan
            Stat("m3", "12345", "2024-25", "8444", "410-karlar", "Valur",    1, 0, 0, 0));  // 2024-25, prio 10, Valur
        SetupTournaments("2025-26",
            Tour("2025-26", "8444", "Olís 25",     10),
            Tour("2025-26", "8437", "Powerade 25", 50));
        SetupTournaments("2024-25", Tour("2024-25", "8444", "Olís 24", 10));

        var result = await CreateSut().GetByPlayerAsync("12345", default);

        Assert.Equal(3, result.Entries.Count);
        Assert.Equal("2025-26", result.Entries[0].Season);
        Assert.Equal("8444", result.Entries[0].TournamentId);
        Assert.Equal("2025-26", result.Entries[1].Season);
        Assert.Equal("8437", result.Entries[1].TournamentId);
        Assert.Equal("2024-25", result.Entries[2].Season);
    }

    [Fact]
    public async Task GetByPlayerAsync_SortTiebreakerOnClubName_CaseInsensitive()
    {
        SetupStats("12345",
            Stat("m1", "12345", "2025-26", "8444", "410-karlar", "valur",    1, 0, 0, 0),
            Stat("m2", "12345", "2025-26", "8444", "385-karlar", "Stjarnan", 1, 0, 0, 0));
        SetupTournaments("2025-26", Tour("2025-26", "8444", "Olís 25", 10));

        var result = await CreateSut().GetByPlayerAsync("12345", default);

        Assert.Equal("Stjarnan", result.Entries[0].ClubName);
        Assert.Equal("valur",    result.Entries[1].ClubName);
    }

    [Theory]
    [InlineData("385-karlar", "385")]
    [InlineData("385",        "385")]
    public async Task GetByPlayerAsync_ClubIdDerivation(string teamId, string expectedClubId)
    {
        SetupStats("12345", Stat("m1", "12345", "2025-26", "8444", teamId, "X", 1, 0, 0, 0));
        SetupTournaments("2025-26", Tour("2025-26", "8444", "Olís 25", 10));

        var result = await CreateSut().GetByPlayerAsync("12345", default);

        Assert.Equal(expectedClubId, result.Entries[0].ClubId);
    }

    [Fact]
    public async Task GetByPlayerAsync_TotalsArithmetic_CrossGroup()
    {
        SetupStats("12345",
            Stat("m1", "12345", "2025-26", "8444", "385-karlar", "Stjarnan", 5, 0, 1, 0),
            Stat("m2", "12345", "2025-26", "8444", "385-karlar", "Stjarnan", 7, 1, 0, 0),
            Stat("m3", "12345", "2025-26", "8437", "385-karlar", "Stjarnan", 3, 0, 0, 1));
        SetupTournaments("2025-26",
            Tour("2025-26", "8444", "Olís 25",     10),
            Tour("2025-26", "8437", "Powerade 25", 50));

        var result = await CreateSut().GetByPlayerAsync("12345", default);

        Assert.NotNull(result.Totals);
        Assert.Equal(3, result.Totals!.Games);
        Assert.Equal(15, result.Totals.TotalGoals);
        Assert.Equal(1,  result.Totals.TotalYellowCards);
        Assert.Equal(1,  result.Totals.TotalTwoMinuteSuspensions);
        Assert.Equal(1,  result.Totals.TotalRedCards);
        Assert.Equal(15.0 / 3, result.Totals.AvgGoals);
        Assert.Equal(1.0  / 3, result.Totals.AvgYellowCards);
    }
}
